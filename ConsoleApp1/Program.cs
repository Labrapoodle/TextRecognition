using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OCR_test
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Start");

            var model2 = LocalFullModels.LatinV5;

            Console.WriteLine("Model loaded");
            string imagePath = "C:\\Users\\k_alejnikov\\source\\repos\\ConsoleApp1\\ConsoleApp1\\bin\\Debug\\net8.0\\image.jpg";

            if (!System.IO.File.Exists(imagePath))
            {
                Console.WriteLine("Файл не найден!");
                return;
            }

            // Регулярное выражение для Кода: 1 латинская буква + от 1 до 4 цифр (например, N9000, P21, S24)
            Regex codeRegex = new Regex(@"^[A-Za-z]\d{1,4}$", RegexOptions.Compiled);

            // Регулярное выражение для извлечения числа (целого или с плавающей точкой)
            Regex valueRegex = new Regex(@"[-+]?\d*\.?\d+", RegexOptions.Compiled);

            Console.WriteLine("Инициализация PaddleOCR...");


            var model = LocalFullModels.CyrillicV5;
            using var ocr = new PaddleOcrAll(model, PaddleDevice.Openblas());
            ocr.AllowRotateDetection = true;
            ocr.Enable180Classification = false;
            using (Mat src = Cv2.ImRead(imagePath))
            {
                Console.WriteLine("Распознавание...");
                PaddleOcrResult ocrResult = ocr.Run(src);

                // Шаг 1: Разделяем блоки на "Коды" и "Все остальные блоки" (кандидаты в значения)
                var codeBlocks = new List<PaddleOcrResultRegion>();
                var otherBlocks = new List<PaddleOcrResultRegion>();

                for (int i = 0; i < ocrResult.Regions.Length; i++)
                {
                    var region = ocrResult.Regions[i];
                    // Очищаем текст от лишних пробелов для точной проверки регуляркой
                    string cleanText = region.Text.Replace(" ", "").Trim();

                    if (codeRegex.IsMatch(cleanText))
                    {
                        region.Text = cleanText; // Сохраняем очищенный код
                        codeBlocks.Add(region);
                    }
                    else
                    {
                        otherBlocks.Add(region);
                    }
                }

                Console.WriteLine($"\nНайдено потенциальных кодов: {codeBlocks.Count}\n");
                Console.WriteLine($"{"Код",-10} | {"Значение",-10} | {"Исходная строка справа",-25}");
                Console.WriteLine(new string('-', 55));

                // Шаг 2: Для каждого кода ищем значение справа
                foreach (var codeBlock in codeBlocks)
                {
                    // Вычисляем центр и границы блока кода для геометрического сопоставления
                    var codeRect = codeBlock.Rect;
                    float codeCenterY = (codeRect.Points()[0].Y + codeRect.Points()[2].Y) / 2f;
                    float codeMaxX = Math.Max(codeRect.Points()[1].X, codeRect.Points()[2].X);
                    float codeHeight = Math.Abs(codeRect.Points()[2].Y - codeRect.Points()[0].Y);

                    // Ищем блоки, которые:
                    // 1. Находятся правее нашего кода (X > codeMaxX - с небольшим допуском)
                    // 2. Находятся примерно на той же горизонтальной линии (по Y)
                    var candidatesRight = otherBlocks
                        .Where(b => {
                            var bRect = b.Rect;
                            float bMinX = Math.Min(bRect.Points()[0].X, bRect.Points()[3].X);
                            float bCenterY = (bRect.Points()[0].Y + bRect.Points()[2].Y) / 2f;

                            // Блок должен быть справа и попадать в коридор высоты кода по вертикали
                            return bMinX > (codeMaxX - 10) && Math.Abs(bCenterY - codeCenterY) < (codeHeight * 0.8f);
                        })
                        // Сортируем по близости к коду (берем самый левый из тех, что справа)
                        .OrderBy(b => Math.Min(b.Rect.Points()[0].X, b.Rect.Points()[3].X))
                        .FirstOrDefault();

                    if (candidatesRight != null)
                    {
                        string rawValueText = candidatesRight.Text;

                        // Если блок содержит знак '=', убираем его и все что левее
                        if (rawValueText.Contains('='))
                        {
                            rawValueText = rawValueText.Substring(rawValueText.IndexOf('=') + 1);
                        }

                        // Вытаскиваем только числовое значение через Regex
                        var match = valueRegex.Match(rawValueText);
                        if (match.Success)
                        {
                            string numericValue = match.Value;
                            Console.WriteLine($"{codeBlock.Text,-10} | {numericValue,-10} | {candidatesRight.Text,-25}");
                        }
                        else
                        {
                            // Текст справа есть, но число не распарсилось
                            Console.WriteLine($"{codeBlock.Text,-10} | {"Ошибка чис.",-10} | {candidatesRight.Text,-25}");
                        }
                    }
                    else
                    {
                        // Если значение не найдено на той же строке
                        Console.WriteLine($"{codeBlock.Text,-10} | {"НД",-10} | {"[Значение не найдено]",-25}");
                    }
                }
            }
        }
    }
}