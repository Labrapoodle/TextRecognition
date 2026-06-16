using OpenCvSharp;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace OCR_test
{
    internal class Program
    {
        // Делаем Main асинхронным (Task вместо void), чтобы использовать await
        static async Task Main(string[] args)
        {
            Console.WriteLine("Запуск распознавания через Windows.Media.Ocr...");

            // 1. Читаем картинку в массив байт
            byte[] imgData;
            try
            {
                using (var file = File.OpenRead("image.jpg"))
                {
                    imgData = new byte[file.Length];
                    file.Read(imgData, 0, imgData.Length);
                }
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine("Ошибка: Не найден файл 'image.jpg' в папке с программой!");
                return;
            }

            try
            {
                // 2. Вызываем асинхронный метод распознавания
                var res = await RecognitionWindowsOcr(imgData);

                // 3. Записываем результаты в файл
                using var sw = new StreamWriter("output.txt", false, System.Text.Encoding.UTF8);

                Console.WriteLine($"\nНайдено строк: {res.Count}");

                for (int i = 0; i < res.Count; i++)
                {
                    // Выводим в консоль для наглядности
                    Console.WriteLine($"[{i + 1}] {res[i].Item2}");

                    // Пишем в файл координаты прямоугольника и текст
                    var rect = res[i].Item1;
                    sw.WriteLine($"X:{rect.X:F0}, Y:{rect.Y:F0}, W:{rect.Width:F0}, H:{rect.Height:F0} -> {res[i].Item2}");
                }

                sw.Flush();
                Console.WriteLine("\nРезультат успешно сохранен в 'output.txt'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nПроизошла ошибка при распознавании: {ex.Message}");
            }
        }

        // Твой метод ProcessImage (оставляем без изменений)
        static (byte[], byte[], byte[]) ProcessImage(byte[] imageBytes)
        {
            using var src = Cv2.ImDecode(imageBytes, ImreadModes.Color);

            using var claheImg = new Mat();
            using (var clahe = Cv2.CreateCLAHE(clipLimit: 3.0, tileGridSize: new OpenCvSharp.Size(8, 8)))
            {
                using var lab = new Mat();
                Cv2.CvtColor(src, lab, ColorConversionCodes.BGR2Lab);

                var channels = Cv2.Split(lab);
                using var lChannel = channels[0];
                using var aChannel = channels[1];
                using var bChannel = channels[2];

                clahe.Apply(lChannel, lChannel);

                using var newLab = new Mat();
                Cv2.Merge(new[] { lChannel, aChannel, bChannel }, newLab);
                Cv2.CvtColor(newLab, claheImg, ColorConversionCodes.Lab2BGR);
            }

            using var blurred = new Mat();
            Cv2.GaussianBlur(claheImg, blurred, new OpenCvSharp.Size(5, 5), 2.0);

            using var gray = new Mat();
            Cv2.CvtColor(blurred, gray, ColorConversionCodes.BGR2GRAY);

            using var background = new Mat();
            Cv2.GaussianBlur(gray, background, new OpenCvSharp.Size(0, 0), 50);

            using var normalized = new Mat();
            Cv2.Absdiff(gray, background, normalized);

            using var blackTextMask = new Mat();
            Cv2.Threshold(normalized, blackTextMask, 51, 255, ThresholdTypes.Binary);
            var bytes1 = blackTextMask.ToBytes(".jpg");

            using var whiteTextMask = new Mat();
            Cv2.Threshold(gray, whiteTextMask, 204, 255, ThresholdTypes.Binary);
            var bytes2 = whiteTextMask.ToBytes(".jpg");

            using var allTextMask = new Mat();
            Cv2.BitwiseOr(blackTextMask, whiteTextMask, allTextMask);

            using var result = new Mat(src.Size(), MatType.CV_8UC1, new Scalar(255));
            result.SetTo(new Scalar(0), allTextMask);

            var bytes3 = result.ToBytes(".jpg");

            return (bytes1, bytes2, bytes3);
        }

        // Новый метод распознавания через Windows API
        public static async Task<System.Collections.Generic.List<(Windows.Foundation.Rect, string)>> RecognitionWindowsOcr(byte[] imgData)
        {
            var corrImgData = ProcessImage(imgData);

            // Сохраняем промежуточные результаты для тестов, как и раньше
            File.WriteAllBytes("postcorrection.jpg", corrImgData.Item1);
            File.WriteAllBytes("postcorrectionN.jpg", corrImgData.Item2);
            File.WriteAllBytes("postcorrectionSum.jpg", corrImgData.Item3);

            // Для Windows OCR лучше всего передавать отбеленный результат (Item3)
            // или нормализованный по контрасту (в зависимости от качества исходника)
            byte[] processedBytes = corrImgData.Item3;

            var resultList = new System.Collections.Generic.List<(Windows.Foundation.Rect, string)>();
            var language = new Windows.Globalization.Language("ru-RU");

            if (!Windows.Media.Ocr.OcrEngine.IsLanguageSupported(language))
            {
                throw new Exception("Русский язык для OCR не установлен в системе Windows! Проверьте параметры языка ОС.");
            }

            var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(language);

            using (var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
            {
                using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(processedBytes);
                    await writer.StoreAsync();
                }

                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                using (Windows.Graphics.Imaging.SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync())
                {
                    Windows.Media.Ocr.OcrResult ocrResult = await engine.RecognizeAsync(softwareBitmap);

                    foreach (var line in ocrResult.Lines)
                    {
                        if (line.Words.Count > 0)
                        {
                            // Берём прямоугольник первого слова и объединяем с остальными, 
                            // чтобы получить рамку для всей строки целиком
                            var firstWordRect = line.Words[0].BoundingRect;
                            double minX = firstWordRect.X;
                            double minY = firstWordRect.Y;
                            double maxX = firstWordRect.X + firstWordRect.Width;
                            double maxY = firstWordRect.Y + firstWordRect.Height;

                            for (int i = 1; i < line.Words.Count; i++)
                            {
                                var wRect = line.Words[i].BoundingRect;
                                if (wRect.X < minX) minX = wRect.X;
                                if (wRect.Y < minY) minY = wRect.Y;
                                if (wRect.X + wRect.Width > maxX) maxX = wRect.X + wRect.Width;
                                if (wRect.Y + wRect.Height > maxY) maxY = wRect.Y + wRect.Height;
                            }

                            var lineRect = new Windows.Foundation.Rect(minX, minY, maxX - minX, maxY - minY);
                            resultList.Add((lineRect, line.Text));
                        }
                    }
                }
            }

            return resultList;
        }
    }
}