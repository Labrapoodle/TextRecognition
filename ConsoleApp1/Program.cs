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
            Mat gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

            // 2. Инвертируем пороговым фильтром (текст должен стать БЕЛЫМ, фон ЧЕРНЫМ)
            Mat thresh = new Mat();
            Cv2.AdaptiveThreshold(gray, thresh, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.BinaryInv, 11, 2);

            // 3. Создаем ГОРИЗОНТАЛЬНОЕ ядро для дилатации (ширина 25, высота 3)
            // Оно склеит буквы в строке, но не склеит строки между собой
            Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(25, 3));
            Mat dilated = new Mat();
            Cv2.Dilate(thresh, dilated, kernel);

            OpenCvSharp.Point[][] contours;
            HierarchyIndex[] hierarchy;
            Cv2.FindContours(dilated, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

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