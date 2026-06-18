using OpenCvSharp;
using System;
using System.Drawing;
using System.IO;
using System.Net.Http.Headers;
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
            // 1. Загрузка изображения
            using var src = Cv2.ImDecode(imageBytes, ImreadModes.Color);

            using var gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);


            Mat blurred = new();
            Cv2.MedianBlur(gray, blurred, 3);

            int h = src.Rows;
            int w = src.Cols;

            byte[,] arr = new byte[h, w];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    arr[y, x] = blurred.At<byte>(y, x);
                }
            }

            

            byte[,] sig = LocalStatisticSigmoidContrast(arr, radius: 15, k: 2.5);

            Mat sigmoidMat =
            new Mat(h, w, MatType.CV_8UC1);

                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            sigmoidMat.Set(y, x, sig[y, x]);
                        }
                    }

            var bytes2 = sigmoidMat.ToBytes(".jpg");

            Mat binary = new Mat();

            Cv2.AdaptiveThreshold(
                sigmoidMat,
                binary,
                255,
                AdaptiveThresholdTypes.GaussianC,
                ThresholdTypes.Binary,
                41,
                10);

            var bytes3 = binary.ToBytes(".jpg");

            return (null, bytes2, bytes3);

    }



        public static byte[,] LocalStatisticSigmoidContrast(
    byte[,] img,
    int radius,
    double k = 2.5)
        {
            int h = img.GetLength(0);
            int w = img.GetLength(1);

            byte[,] result = new byte[h, w];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int y0 = Math.Max(0, y - radius);
                    int y1 = Math.Min(h - 1, y + radius);

                    int x0 = Math.Max(0, x - radius);
                    int x1 = Math.Min(w - 1, x + radius);

                    double sum = 0;
                    double sum2 = 0;
                    int count = 0;

                    // собираем статистику окна
                    for (int yy = y0; yy <= y1; yy++)
                    {
                        for (int xx = x0; xx <= x1; xx++)
                        {
                            double v = img[yy, xx];

                            sum += v;
                            sum2 += v * v;
                            count++;
                        }
                    }

                    double mean = sum / count;

                    double variance =
                        sum2 / count - mean * mean;

                    if (variance < 0)
                        variance = 0;

                    double std = Math.Sqrt(variance);

                    // защита от деления на ноль
                    if (std < 1.0)
                    {
                        result[y, x] = img[y, x];
                        continue;
                    }

                    // z-score
                    double z =
                        (img[y, x] - mean) / std;

                    // сигмоида
                    double ySig =
                        1.0 /
                        (1.0 + Math.Exp(-k * z));

                    result[y, x] =
                        (byte)Math.Clamp(
                            (int)(255.0 * ySig),
                            0,
                            255);
                }
            }

            return result;
        }


        public static List<(Tesseract.Rect, string)> Recognition(string TessDataPath, byte[] imgData)
        {
            //string imagePath = "screen.jpg"; // Путь к фото
            //string tessData = @"C:\tessdata\"; // Путь к данным Tesseract (скачать tessdata)

            var corrImgData = ProcessImage(imgData);
            //File.WriteAllBytes("postcorrection.png", corrImgData.Item1);
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