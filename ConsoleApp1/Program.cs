using OpenCvSharp;
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Tesseract;
using System.Text.RegularExpressions;

namespace OCR_test
{
    internal class Program
    {
        static void Main(string[] args)
        {
            
            

            // 1. Загружаем и переводим в ч/б 
            Mat src = Cv2.ImRead("image.jpg");

            //Mat preprocessed = new();
            //preprocessed = ImPreProcess(src);

            Mat gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

            

            // 2. Инвертируем пороговым фильтром (текст должен стать БЕЛЫМ, фон ЧЕРНЫМ)
            Mat thresh = new Mat();
            Cv2.AdaptiveThreshold(gray, thresh, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.BinaryInv, 11, 2);


            Mat transformed = new();
            transformed = transform(src);
            if (transformed != null) 
            { 
                thresh = transformed;
                File.WriteAllBytes("TRANSFORMED.jpg", transformed.ToBytes(".jpg"));
            }
            Mat preprocessed = new();
            preprocessed = ImPreProcess(thresh);

            // 3. Создаем ГОРИЗОНТАЛЬНОЕ ядро для дилатации (ширина 25, высота 3)
            // Оно склеит буквы в строке, но не склеит строки между собой
            Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(25, 3));
            Mat dilated = new Mat();
            Cv2.Dilate(preprocessed, dilated, kernel);

            File.WriteAllBytes("DILATED.jpg", dilated.ToBytes(".jpg"));


            OpenCvSharp.Point[][] contours;
            HierarchyIndex[] hierarchy;
            Cv2.FindContours(preprocessed, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            foreach (var contour in contours)
            {
                OpenCvSharp.Rect rect = Cv2.BoundingRect(contour);

                // Фильтруем слишком мелкие объекты (шум) и слишком квадратные
                // Нам нужны длинные узкие полосы (строки текста)
                if (rect.Width > 50 && rect.Height > 10 && rect.Height < 50)
                {
                    // Вырезаем ОРИГИНАЛЬНЫЙ кусочек изображения по этим координатам
                    Mat roi = new Mat(gray, rect);

                    // Передаем этот кусочек в функцию проверки текста (Шаг 3)
                    CheckZoneContent(roi, rect);
                }
            }
        }


        static Mat ImPreProcess(Mat src)
        {


            // File.WriteAllBytes("postcorrection.png", corrImgData.Item1);


            // 1. Загрузка изображения
           







            Mat gray = new();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

            //var bytes1 = gray.ToBytes(".jpg");

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
            

            


            var kernel = Cv2.GetStructuringElement(
                MorphShapes.Rect,
                new OpenCvSharp.Size(5, 5));

            Cv2.MorphologyEx(
                binary,
                binary,
                MorphTypes.Close,
                kernel);
            return binary;
        }

        static Mat transform(Mat src)
        {


            Mat gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

            // 1. Выделяем границы, убирая мелкий шум матрицы монитора
            Mat blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(3, 3), 0);
            Mat edges = new Mat();
            Cv2.Canny(src, edges, 80, 200);

            // 2. Ищем ВСЕ линии на изображении
            LineSegmentPoint[] lines = Cv2.HoughLinesP(edges, 1, Math.PI / 180, 50, 80, 20);

            List<LineSegmentPoint> horizontals = new List<LineSegmentPoint>();
            List<LineSegmentPoint> verticals = new List<LineSegmentPoint>();

            foreach (var line in lines)
            {
                double deltaX = line.P2.X - line.P1.X;
                double deltaY = line.P2.Y - line.P1.Y;
                double angle = Math.Atan2(deltaY, deltaX) * (180.0 / Math.PI);
                double absAngle = Math.Abs(angle);

                // Группируем линии на "почти горизонтальные" и "почти вертикальные"
                if (absAngle < 15 || absAngle > 165)
                    horizontals.Add(line);
                else if (absAngle > 75 && absAngle < 105)
                    verticals.Add(line);
            }

            // 3. Находим крайние линии, которые образуют "рамку" вокруг контента
            // Для этого сортируем их по координатам в пространстве
            var top = horizontals.OrderBy(l => Math.Min(l.P1.Y, l.P2.Y)).FirstOrDefault();
            var bottom = horizontals.OrderByDescending(l => Math.Max(l.P1.Y, l.P2.Y)).FirstOrDefault();
            var left = verticals.OrderBy(l => Math.Min(l.P1.X, l.P2.X)).FirstOrDefault();
            var right = verticals.OrderByDescending(l => Math.Max(l.P1.X, l.P2.X)).FirstOrDefault();

            if (top.P1 != default && bottom.P1 != default && left.P1 != default && right.P1 != default)
            {
                // 4. Находим 4 точки пересечения этих крайних линий (математические углы трапеции)
                Point2f[] srcPoints = new Point2f[4];
                srcPoints[0] = GetIntersection(top, left);     // Левый верх
                srcPoints[1] = GetIntersection(top, right);    // Правый верх
                srcPoints[2] = GetIntersection(bottom, right); // Правый низ
                srcPoints[3] = GetIntersection(bottom, left);  // Левый низ

                // 5. Проекция в ровный прямоугольник
                int width = 1024;
                int height = 768;
                Point2f[] dstPoints = new Point2f[]
                {
        new Point2f(0, 0),
        new Point2f(width - 1, 0),
        new Point2f(width - 1, height - 1),
        new Point2f(0, height - 1)
                };

                Mat transformMatrix = Cv2.GetPerspectiveTransform(srcPoints, dstPoints);
                Mat output = new Mat();
                Cv2.WarpPerspective(src, output, transformMatrix, new OpenCvSharp.Size(width, height), InterpolationFlags.Cubic);

                return output;
                Console.WriteLine("Перспектива успешно выровнена по текстовым направляющим!");
            }
            else
            {
                Console.WriteLine("Не удалось собрать каркас линий. Попробуйте подкрутить параметры Canny/HoughLinesP.");
                return null;
            }
        }

        static Point2f GetIntersection(LineSegmentPoint line1, LineSegmentPoint line2)
        {
            double x1 = line1.P1.X, y1 = line1.P1.Y, x2 = line1.P2.X, y2 = line1.P2.Y;
            double x3 = line2.P1.X, y3 = line2.P1.Y, x4 = line2.P2.X, y4 = line2.P2.Y;

            double den = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (den == 0) return new Point2f(0, 0); // Линии параллельны

            double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / den;
            return new Point2f((float)(x1 + t * (x2 - x1)), (float)(y1 + t * (y2 - y1)));
        }

        // Вспомогательный метод для сортировки углов контура


        static void CheckZoneContent(Mat roi, OpenCvSharp.Rect coordinates)
        {
            // 1. Настраиваем Tesseract под конкретную задачу (Вайтлист для этой фазы)
            // Разрешаем буквы и цифры, так как мы ищем идентификатор типа "S24"
            using (var engine = new TesseractEngine(@"./tessdata", "rus+eng", EngineMode.Default))
            {
                engine.SetVariable("tessedit_char_whitelist", "0123456789.,=abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ");

                // Конвертируем Mat в формат, понятный Tesseract (например, через массив байт или Pix)
                using (var img = Pix.LoadFromMemory(roi.ToBytes(".png")))
                {
                    using (var page = engine.Process(img))
                    {
                        string text = page.GetText().Trim();

                        // 2. Ищем с помощью Regular Expressions
                        // Шаблон: Буква S (или близкая 5), затем цифры, затем знак равно, затем цифры с точкой
                        // Пример: S24 = 110.5
                        string pattern = @"[S5s][0-9]+.*=.*[0-9]+\.[0-9]+";

                        if (Regex.IsMatch(text, pattern))
                        {
                            Console.WriteLine($"Ура! Найдена нужная зона по координатам X:{coordinates.X}, Y:{coordinates.Y}");
                            Console.WriteLine($"Текст в зоне: {text}");

                            // Здесь вы можете запустить финальное, более точное распознавание 
                            // или просто распарсить значение "110.5" из строки `text`
                        }
                    }
                }
            }
        }


    }
}