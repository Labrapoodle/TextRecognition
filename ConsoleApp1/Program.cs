using OpenCvSharp;
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Tesseract;

namespace OCR_test
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var path = Assembly.GetExecutingAssembly().Location;
            var tessPath = Path.Combine(Path.GetDirectoryName(path), "TessData");
            byte[] imgData;
            using (var file = File.OpenRead("image.jpg"))
            {
                imgData = new byte[file.Length];
                file.Read(imgData, 0, imgData.Length);
            }

            var res = Recognition(tessPath, imgData);
            using var sw = new StreamWriter("output.txt", false);
            for (int i = 0; i < res.Count; i++)
            {
                sw.Write($"{res[i].Item1}: {res[i].Item2}");
            }
            sw.Flush();
        }

        

        
        static (byte[], byte[], byte[]) ProcessImage(byte[] imageBytes)
        {
            // File.WriteAllBytes("postcorrection.png", corrImgData.Item1);


            // 1. Загрузка изображения
            using var src = Cv2.ImDecode(imageBytes, ImreadModes.Color);




            


            Mat gray = new();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

            var bytes1 = gray.ToBytes(".jpg");

            Mat filtered = new();
            Cv2.MedianBlur(gray, filtered, 5);

            var bytes2 = filtered.ToBytes(".jpg");

            Mat denoise = new();
            Cv2.FastNlMeansDenoising(
                filtered,
                denoise,
                15); // 10-20 обычно

            var bytes3 = denoise.ToBytes(".jpg");

            Mat blur = new();
            Cv2.GaussianBlur(
                denoise,
                blur,
                new OpenCvSharp.Size(3, 3),
                0);

            
            Mat contrast = new();
            var clahe = Cv2.CreateCLAHE(2.0, new OpenCvSharp.Size(8, 8));
            clahe.Apply(blur, contrast);
            File.WriteAllBytes("BeforeOldTreshold.jpg", contrast.ToBytes(".jpg"));

            
            Mat binary = new();
            Cv2.AdaptiveThreshold(
                contrast,
                binary,
                255,
                AdaptiveThresholdTypes.GaussianC,
                ThresholdTypes.Binary,
                31,
                10);
            CannY(binary);

            File.WriteAllBytes("AfterOldTreshold.jpg", binary.ToBytes(".jpg"));


            var kernel = Cv2.GetStructuringElement(
                MorphShapes.Rect,
                new OpenCvSharp.Size(5, 5));

            Cv2.MorphologyEx(
                binary,
                binary,
                MorphTypes.Close,
                kernel);

            

            File.WriteAllBytes("Result.png", binary.ToBytes(".jpg"));

            return (bytes1, bytes2, bytes3);

    }
         static float Distance(Point2f p1, Point2f p2)
        {
            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;

            return MathF.Sqrt(dx * dx + dy * dy);
        }
        static Point2f[] OrderCorners(OpenCvSharp.Point[] pts)
        {
            var p = pts.Select(x => new Point2f(x.X, x.Y)).ToArray();

            Point2f tl = p.OrderBy(v => v.X + v.Y).First();
            Point2f br = p.OrderByDescending(v => v.X + v.Y).First();

            Point2f tr = p.OrderBy(v => v.Y - v.X).First();
            Point2f bl = p.OrderByDescending(v => v.Y - v.X).First();

            return new[] { tl, tr, br, bl };
        }

        static void CannY(Mat src)
        {
            //var gray2 = new Mat();
            //Cv2.CvtColor(src, gray2, ColorConversionCodes.BGR2GRAY);

            //var blurred2 = new Mat();
            //Cv2.GaussianBlur(src, blurred2, new OpenCvSharp.Size(5, 5), 0);

            var edges2 = new Mat();
            Cv2.Canny(src, edges2, 0, 0);
            File.WriteAllBytes("EDGES.jpg", edges2.ToBytes(".jpg", new ImageEncodingParam(ImwriteFlags.JpegQuality, 100)));

            Cv2.FindContours(
                edges2,
                out OpenCvSharp.Point[][] contours,
                out HierarchyIndex[] hierarchy,
                RetrievalModes.List,
                ContourApproximationModes.ApproxSimple);


            OpenCvSharp.Point[] screenContour = null;
            double maxArea = 0;

            foreach (var contour in contours)
            {
                double peri = Cv2.ArcLength(contour, true);

                var approx = Cv2.ApproxPolyDP(
                    contour,
                    0.02 * peri,
                    true);

                if (approx.Length == 4)
                {
                    double area = Cv2.ContourArea(approx);

                    if (area > maxArea)
                    {
                        maxArea = area;
                        screenContour = approx;
                    }
                }
            }


            var corners = OrderCorners(screenContour);

            float width =
                Math.Max(
                    Distance(corners[0], corners[1]),
                    Distance(corners[2], corners[3]));

            float height =
                Math.Max(
                    Distance(corners[0], corners[3]),
                    Distance(corners[1], corners[2]));


            var dstCorners = new[]
                {
                    new Point2f(0, 0),
                    new Point2f(width - 1, 0),
                    new Point2f(width - 1, height - 1),
                    new Point2f(0, height - 1)
                };

            var matrix =
                Cv2.GetPerspectiveTransform(
                    corners,
                    dstCorners);

            var warped = new Mat();

            Cv2.WarpPerspective(
                src,
                warped,
                matrix,
                new OpenCvSharp.Size((int)width, (int)height));

            File.WriteAllBytes("WARPED.jpg", warped.ToBytes(".jpg"));
        }

        public static List<(Tesseract.Rect, string)> Recognition(string TessDataPath, byte[] imgData)
        {
            //string imagePath = "screen.jpg"; // Путь к фото
            //string tessData = @"C:\tessdata\"; // Путь к данным Tesseract (скачать tessdata)

            var corrImgData = ProcessImage(imgData);
            File.WriteAllBytes("postcorrection.png", corrImgData.Item1);
            File.WriteAllBytes("postcorrectionN.png", corrImgData.Item2);
            File.WriteAllBytes("postcorrectionSum.png", corrImgData.Item3);

            //var corrImgDataN = ProcessImageNeg(imgData);
            //File.WriteAllBytes("postcorrectionN.jpg", corrImgDataN);

            List<(Tesseract.Rect, string)> result = new System.Collections.Generic.List<(Tesseract.Rect, string)>();
            using (var engine = new TesseractEngine(TessDataPath, "rus+eng", EngineMode.LstmOnly))
            using (var img = Pix.LoadFromMemory(corrImgData.Item3))
            using (var page = engine.Process(img))
            {
                using (var iter = page.GetIterator())
                {
                    iter.Begin();
                    do
                    {
                        if (iter.IsAtBeginningOf(PageIteratorLevel.TextLine))
                        {
                            var text = iter.GetText(PageIteratorLevel.TextLine);
                            if (iter.TryGetBoundingBox(PageIteratorLevel.TextLine, out Tesseract.Rect bbox))
                            {
                                result.Add((bbox, text));
                            }
                        }
                    } while (iter.Next(PageIteratorLevel.TextLine));
                }
            }

            return result;
        }


    }
}