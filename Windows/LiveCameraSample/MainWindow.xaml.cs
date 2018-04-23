// 
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license.
// 
// Microsoft Cognitive Services: http://www.microsoft.com/cognitive
// 
// Microsoft Cognitive Services Github:
// https://github.com/Microsoft/Cognitive
// 
// Copyright (c) Microsoft Corporation
// All rights reserved.
// 
// MIT License:
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// 

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Diagnostics;
using Newtonsoft.Json;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Microsoft.ProjectOxford.Emotion;
using Microsoft.ProjectOxford.Emotion.Contract;
using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;
using Microsoft.ProjectOxford.Vision;
using VideoFrameAnalyzer;
using Microsoft.Azure.EventHubs;
using System.Text;
using System.Net.Http;
using System.Net;
using System.IO;
using System.Runtime.InteropServices;

namespace LiveCameraSample
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        private EmotionServiceClient _emotionClient = null;
        private FaceServiceClient _faceClient = null;
        private VisionServiceClient _visionClient = null;
        private readonly FrameGrabber<LiveCameraResult> _grabber = null;
        private static readonly ImageEncodingParam[] s_jpegParams = {
            new ImageEncodingParam(ImwriteFlags.JpegQuality, 60)
        };
        private readonly CascadeClassifier _localFaceDetector = new CascadeClassifier();
        private bool _fuseClientRemoteResults;
        private LiveCameraResult _latestResultsToDisplay = null;
        private AppMode _mode;
        private DateTime _startTime;
        private int grabberDelay;

        private static EventHubClient _eventHubClient;
        private const string EhEntityPath = "faces";

        public enum AppMode
        {
            Faces,
            Emotions,
            EmotionsWithClientFaceDetect,
            Tags,
            Celebrities
        }
        private double variance(int[] nums)
        {
            if (nums.Length > 1)
            {

                // Get the average of the values
                double avg = getAverage(nums);

                // Now figure out how far each point is from the mean
                // So we subtract from the number the average
                // Then raise it to the power of 2
                double sumOfSquares = 0.0;

                foreach (int num in nums)
                {
                    sumOfSquares += Math.Pow((num - avg), 2.0);
                }

                // Finally divide it by n - 1 (for standard deviation variance)
                // Or use length without subtracting one ( for population standard deviation variance)
                return sumOfSquares / (double)(nums.Length - 1);
            }
            else { return 0.0; }
        }
        // Get the average of our values in the array
        private double getAverage(int[] nums)
        {
            int sum = 0;

            if (nums.Length > 1)
            {

                // Sum up the values
                foreach (int num in nums)
                {
                    sum += num;
                }

                // Divide by the number of values
                return sum / (double)nums.Length;
            }
            else { return (double)nums[0]; }
        }
        public MainWindow()
        {
            InitializeComponent();

            grabberDelay = 0;

            // Create grabber. 
            _grabber = new FrameGrabber<LiveCameraResult>();

            // Set up a listener for when the client receives a new frame.
            _grabber.NewFrameProvided += (s, e) =>
            {

                // The callback may occur on a different thread, so we must use the
                // MainWindow.Dispatcher when manipulating the UI. 
                this.Dispatcher.BeginInvoke((Action)(() =>
                {
                    // Display the image in the left pane.
                    LeftImage.Source = e.Frame.Image.ToBitmapSource();


                    //TODO: some magic arround VisionAPI
                    //###########
                   
                   

                    
                    //RightImage.Source = VisualizeResult(e.Frame);
                }));

                // See if auto-stop should be triggered. 
                if (Properties.Settings.Default.AutoStopEnabled && (DateTime.Now - _startTime) > Properties.Settings.Default.AutoStopTime)
                {
                    _grabber.StopProcessingAsync();
                }
            };

            // Set up a listener for when the client receives a new result from an API call. 
            _grabber.NewResultAvailable += (s, e) =>
            {
                this.Dispatcher.BeginInvoke((Action)(() =>
                {
                    if (e.TimedOut)
                    {
                        MessageArea.Text = "API call timed out.";
                    }
                    else if (e.Exception != null)
                    {
                        string apiName = "API";
                        string message = e.Exception.Message;

                        MessageArea.Text = string.Format("{0} API call failed on frame {1}. Exception: {2}", apiName, e.Frame.Metadata.Index, message);
                    }
                    else
                    {
                        _latestResultsToDisplay = e.Analysis;

                        RightImage.Source = VisualizeResult(e.Frame);

                    }
                }));
            };

            // Create local face detector. 
            _localFaceDetector.Load("Data/haarcascade_frontalface_alt2.xml");
        }

        /// <summary> Function which submits a frame to the Emotion API. </summary>
        /// <param name="frame"> The video frame to submit. </param>
        /// <returns> A <see cref="Task{LiveCameraResult}"/> representing the asynchronous API call,
        ///     and containing the emotions returned by the API. </returns>
        private async Task<LiveCameraResult> ImageAnalysisFunction(VideoFrame frame)
        {
            // Encode image. 
            var jpg = frame.Image.ToMemoryStream(".jpg", s_jpegParams);
            //OutputArray x = null;
            Mat m = frame.Image.CvtColor(ColorConversionCodes.BGR2GRAY).Laplacian(MatType.CV_64F);
            Mat mean = new Mat(), stddev = new Mat();
            m.MeanStdDev(mean, stddev);
            double variance = Math.Pow(stddev.At<double>(0), 2);
            
            var arr = new List<ResultItem>();

            var strImg = System.Convert.ToBase64String(jpg.ToArray());

            // save input image
            string folderName = @"C:\TMP\IMGs\testx\1out\";

            File.WriteAllBytes(folderName + "ORIG_" + DateTime.Now.ToString("yyyy-MM-dd__HH-mm-ss") + "_var_"+ variance  + ".jpg", Convert.FromBase64String(strImg));

            // Submit image to API. 
            //send transformed XML to server
            using (var client = new HttpClient())
            {
                var content = new StringContent(strImg, Encoding.UTF8, "application/json");

                //CPU
                //var result = await client.PostAsync("http://51.145.152.160:32770/score", content);
                //CPU FAST
                //http://51.144.49.190:32779/score
                var result = await client.PostAsync(Properties.Settings.Default.VisionAPIKey + "/score", content);
                
                //GPU
                //var result = await client.PostAsync("http://52.171.198.226:32770/score", content);

                result.EnsureSuccessStatusCode();
                string resultContent = "";
                if (result.StatusCode == HttpStatusCode.OK)
                //if (true)
                {
                    resultContent = await result.Content.ReadAsStringAsync();
                    //resultContent = " | label: collector-112, box: [342, 212, 377, 304] | label: collector-110116, box: [291, 45, 337, 132] | label: collector-113115, box: [25, 190, 70, 257] | label: defect-yellow-107108109, box: [9, 22, 69, 46] | label: collector-111114, box: [238, 221, 284, 278]";
                    resultContent = resultContent.Replace("\"\\\"", "");
                    resultContent = resultContent.Replace("\\\"\"", "");
                    foreach(string i in resultContent.Split('|'))
                    {
                        if (i.Trim().Length > 0 && i.Split(',').Length>0)
                        {
                            var itm = new ResultItem();
                            //| label: r , box: [343, 425, 378, 459] 
                            itm.Label = i.Split(',')[0].Trim().Replace("label: ", "");
                            itm.Label = itm.Label ;
                            var x = i.Split('[')[1].Trim().Replace("]", "").Split(',');
                            itm.Box = new System.Windows.Rect(
                                int.Parse(x[0]),
                                int.Parse(x[1]),
                                int.Parse(x[2]) - int.Parse(x[0]),
                                int.Parse(x[3]) - int.Parse(x[1]));
                            arr.Add(itm);
                        }
                    }
                }
            }


            // Output. 
            var ret = new LiveCameraResult();
            ret.Items = arr.ToArray<ResultItem>();
            return ret;
        }
        
        private BitmapSource VisualizeResult(VideoFrame frame)
        {
            // Draw any results on top of the image. 
            BitmapSource visImage = frame.Image.ToBitmapSource();

            var result = _latestResultsToDisplay;

            if (result != null)
            {
                visImage = Visualization.DrawResults(visImage, result);
            }

            //save output image

            string folderName = @"C:\TMP\IMGs\testx\2in\";

            using (var fileStream = new FileStream(folderName + "SCORED_"+ DateTime.Now.ToString("yyyy-MM-dd__HH-mm-ss") + ".jpg", FileMode.Create))
            {
                BitmapEncoder encoder = new JpegBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(visImage));
                encoder.Save(fileStream);
            }
            
            return visImage;
        }

        /// <summary> Populate CameraList in the UI, once it is loaded. </summary>
        /// <param name="sender"> Source of the event. </param>
        /// <param name="e">      Routed event information. </param>
        private void CameraList_Loaded(object sender, RoutedEventArgs e)
        {
            int numCameras = _grabber.GetNumCameras();

            if (numCameras == 0)
            {
                MessageArea.Text = "No cameras found!";
            }

            var comboBox = sender as ComboBox;
            comboBox.ItemsSource = Enumerable.Range(0, numCameras).Select(i => string.Format("Camera {0}", i + 1));
            comboBox.SelectedIndex = 0;
        }

        /// <summary> Populate ModeList in the UI, once it is loaded. </summary>
        /// <param name="sender"> Source of the event. </param>
        /// <param name="e">      Routed event information. </param>
        private void ModeList_Loaded(object sender, RoutedEventArgs e)
        {
            var modes = (AppMode[])Enum.GetValues(typeof(AppMode));

            var comboBox = sender as ComboBox;
            comboBox.ItemsSource = modes.Select(m => m.ToString());
            comboBox.SelectedIndex = 0;
        }

        private void ModeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _fuseClientRemoteResults = false;
            _grabber.AnalysisFunction = ImageAnalysisFunction;
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CameraList.HasItems)
            {
                MessageArea.Text = "No cameras found; cannot start processing";
                return;
            }

            // Clean leading/trailing spaces in API keys. 
            //Properties.Settings.Default.FaceAPIKey = Properties.Settings.Default.FaceAPIKey.Trim();
            //Properties.Settings.Default.EmotionAPIKey = Properties.Settings.Default.EmotionAPIKey.Trim();
            //Properties.Settings.Default.VisionAPIKey = Properties.Settings.Default.VisionAPIKey.Trim();

            // Create API clients. 
            //_faceClient = new FaceServiceClient(Properties.Settings.Default.FaceAPIKey);
            //_emotionClient = new EmotionServiceClient(Properties.Settings.Default.EmotionAPIKey);

            // Creates an EventHubsConnectionStringBuilder object from the connection string, and sets the EntityPath.
            // Typically, the connection string should have the entity path in it, but for the sake of this simple scenario
            // we are using the connection string from the namespace.
            //var connectionStringBuilder = new EventHubsConnectionStringBuilder(Properties.Settings.Default.VisionAPIKey)
            //{
            //    EntityPath = EhEntityPath
            //};
            //_eventHubClient = EventHubClient.CreateFromConnectionString(connectionStringBuilder.ToString());

            // How often to analyze. 
            _grabber.TriggerAnalysisOnInterval(Properties.Settings.Default.AnalysisInterval);

            // Reset message. 
            MessageArea.Text = "";

            // Record start time, for auto-stop
            _startTime = DateTime.Now;

            await _grabber.StartProcessingCameraAsync(CameraList.SelectedIndex);
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            await _grabber.StopProcessingAsync();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = 1 - SettingsPanel.Visibility;
        }

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = Visibility.Hidden;
            Properties.Settings.Default.Save();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        private Face CreateFace(FaceRectangle rect)
        {
            return new Face
            {
                FaceRectangle = new FaceRectangle
                {
                    Left = rect.Left,
                    Top = rect.Top,
                    Width = rect.Width,
                    Height = rect.Height
                }
            };
        }

        private Face CreateFace(Microsoft.ProjectOxford.Vision.Contract.FaceRectangle rect)
        {
            return new Face
            {
                FaceRectangle = new FaceRectangle
                {
                    Left = rect.Left,
                    Top = rect.Top,
                    Width = rect.Width,
                    Height = rect.Height
                }
            };
        }

        private Face CreateFace(Microsoft.ProjectOxford.Common.Rectangle rect)
        {
            return new Face
            {
                FaceRectangle = new FaceRectangle
                {
                    Left = rect.Left,
                    Top = rect.Top,
                    Width = rect.Width,
                    Height = rect.Height
                }
            };
        }

    }
}
