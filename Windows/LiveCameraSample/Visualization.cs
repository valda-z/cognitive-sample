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
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ProjectOxford.Emotion;
using Microsoft.ProjectOxford.Face.Contract;
using Microsoft.ProjectOxford.Vision.Contract;

namespace LiveCameraSample
{
    public class Visualization
    {
        private static SolidColorBrush s_lineBrush = new SolidColorBrush(new System.Windows.Media.Color { R = 255, G = 185, B = 0, A = 255 });
        private static SolidColorBrush s_lineBrush_Defect = new SolidColorBrush(new System.Windows.Media.Color { R = 255, G = 0, B = 0, A = 255 });
        private static SolidColorBrush s_lineBrush_Safety = new SolidColorBrush(new System.Windows.Media.Color { R = 200, G = 0, B = 255, A = 255 });
        private static SolidColorBrush s_lineBrush_Safety_Area = new SolidColorBrush(new System.Windows.Media.Color { R = 200, G = 0, B = 255, A = 55 });

        private static Typeface s_typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        private static BitmapSource DrawOverlay(BitmapSource baseImage, Action<DrawingContext, double> drawAction)
        {
            double annotationScale = baseImage.PixelHeight / 320;

            DrawingVisual visual = new DrawingVisual();
            DrawingContext drawingContext = visual.RenderOpen();

            drawingContext.DrawImage(baseImage, new Rect(0, 0, baseImage.Width, baseImage.Height));

            drawAction(drawingContext, annotationScale);

            drawingContext.Close();

            RenderTargetBitmap outputBitmap = new RenderTargetBitmap(
                baseImage.PixelWidth, baseImage.PixelHeight,
                baseImage.DpiX, baseImage.DpiY, PixelFormats.Pbgra32);

            outputBitmap.Render(visual);

            return outputBitmap;
        }
        
        public static BitmapSource DrawResults(BitmapSource baseImage, LiveCameraResult results)
        {
            if (results == null || results.Items == null)
            {
                return baseImage;
            }

            Action<DrawingContext, double> drawAction = (drawingContext, annotationScale) =>
            {
                StreamGeometry streamGeometry = new StreamGeometry();
                PointCollection points = new PointCollection();
                int points_num = 0;

                using (StreamGeometryContext geometryContext = streamGeometry.Open())
                {
                    for (int i = 0; i < results.Items.Length; i++)
                    {
                        var item = results.Items[i];
                        if (item.Box == null) { continue; }
                        if (!item.Label.StartsWith("red", true, CultureInfo.CurrentCulture)) { continue; }

                        Rect itemRect = new Rect(
                            item.Box.Left, item.Box.Top,
                            item.Box.Width, item.Box.Height);

                        System.Windows.Point p = new System.Windows.Point(item.Box.Left + item.Box.Width / 2, item.Box.Top + item.Box.Height / 2);
                        
                        if (points_num == 0)
                        {
                            geometryContext.BeginFigure(p, true, true);
                        } else
                        {
                            points.Add(p);
                        }
                        points_num++;
                        geometryContext.PolyLineTo(points, true, true);
                    }
                }
                streamGeometry.Freeze();
                // if there are more than 3 points... area
                if ( points_num >= 3 )
                {
                    drawingContext.DrawGeometry(s_lineBrush_Safety_Area, new Pen(s_lineBrush_Safety_Area, 2), streamGeometry);
                }
                for (int i = 0; i < results.Items.Length; i++)
                {
                    var item = results.Items[i];
                    if (item.Box == null) { continue; }

                    Rect itemRect = new Rect(
                        item.Box.Left, item.Box.Top,
                        item.Box.Width, item.Box.Height);
                    string text = item.Label;

                    itemRect.Inflate(6 * annotationScale, 6 * annotationScale);

                    double lineThickness = 4 * annotationScale;

                    
                    if (text.StartsWith("Defect", true, CultureInfo.CurrentCulture))
                    {
                        drawingContext.DrawRectangle(Brushes.Transparent, new Pen(s_lineBrush_Defect, lineThickness), itemRect);
                    }
                    else if (text.StartsWith("safety", true, CultureInfo.CurrentCulture))
                    {
                        drawingContext.DrawRectangle(Brushes.Transparent, new Pen(s_lineBrush_Safety, lineThickness), itemRect);
                    }
                    else
                    {
                        drawingContext.DrawRectangle(Brushes.Transparent, new Pen(s_lineBrush, lineThickness), itemRect);
                    }

                    if (text != "")
                    {
                        FormattedText ft = new FormattedText(text,
                            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, s_typeface,
                            16 * annotationScale, Brushes.Black);
                        
                        var pad = 3 * annotationScale;
                        
                        var ypad = pad;
                        var xpad = pad + 4 * annotationScale;
                        var origin = new System.Windows.Point(
                            itemRect.Left + xpad - lineThickness / 2,
                            itemRect.Top - ft.Height - ypad + lineThickness / 2);
                        var rect = ft.BuildHighlightGeometry(origin).GetRenderBounds(null);
                        rect.Inflate(xpad, ypad);

                        //if (text.StartsWith("Defect", true, CultureInfo.CurrentCulture))
                        //{
                        //    drawingContext.DrawRectangle(s_lineBrush_Defect, null, rect);
                        //}
                        //else if (text.StartsWith("safety", true, CultureInfo.CurrentCulture))
                        //{
                        //    drawingContext.DrawRectangle(s_lineBrush_Safety, null, rect);

                        //}
                        //else
                        //{
                            drawingContext.DrawRectangle(s_lineBrush, null, rect);
                        //}
                        
                        drawingContext.DrawText(ft, origin);
                    }
                }
            };

            return DrawOverlay(baseImage, drawAction);
        }
    }
}
