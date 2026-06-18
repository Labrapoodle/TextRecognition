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
            // 1. Загрузка изображения
            using var src = Cv2.ImDecode(imageBytes, ImreadModes.Color);


            int h = src.Rows;
            int w = src.Cols;

            byte[,] arr = new byte[h, w];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    arr[y, x] = src.At<byte>(y, x);
                }
            }

            byte[,] sig = LocalSigmoidContrast(arr, radius: 15, k: 10.0);

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



        public static byte[,] LocalSigmoidContrast(
    byte[,] img,
    int radius,
    double k)
        {
            int h = img.GetLength(0);
            int w = img.GetLength(1);

            byte[,] result = new byte[h, w];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int min = 255;
                    int max = 0;

                    for (int yy = Math.Max(0, y - radius);
                         yy <= Math.Min(h - 1, y + radius);
                         yy++)
                    {
                        for (int xx = Math.Max(0, x - radius);
                             xx <= Math.Min(w - 1, x + radius);
                             xx++)
                        {
                            byte v = img[yy, xx];

                            if (v < min) min = v;
                            if (v > max) max = v;
                        }
                    }

                    if (max == min)
                    {
                        result[y, x] = img[y, x];
                        continue;
                    }

                    double xNorm =
                        (img[y, x] - min) /
                        (double)(max - min);

                    double ySig =
                        1.0 /
                        (1.0 + Math.Exp(-k * (xNorm - 0.5)));

                    result[y, x] =
                        (byte)(255.0 * ySig);
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