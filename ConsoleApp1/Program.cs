using OpenCvSharp;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        // Путь к папке с картинками и файл для записи результатов


        string outputFile = Path.Combine(AppContext.BaseDirectory, "text.txt");

        /*
        string folderPath = @"C:\Users\k_alejnikov\Pictures\takes";
        // Проверяем, существует ли папка
        if (!Directory.Exists(folderPath))
        {
            Console.WriteLine($"Ошибка: Папка не найдена по пути {folderPath}");
            return;
        }

        // Получаем все изображения из папки (jpg, jpeg, png)
        string[] extensions = { "*.jpg", "*.jpeg", "*.png" };
        var imageFiles = new System.Collections.Generic.List<string>();
        foreach (var ext in extensions)
        {
            imageFiles.AddRange(Directory.GetFiles(folderPath, ext));
        }
        */

        string imagePath = @"C:\Users\k_alejnikov\Pictures\takes\example.jpg";

        if (!File.Exists(imagePath))
        {
            Console.WriteLine($"Ошибка: Файл не найден: {imagePath}");
            return;
        }

        var imageFiles = new List<string> { imagePath };

        

        if (imageFiles.Count == 0)
        {
            Console.WriteLine("В папке не найдено изображений для обработки.");
            return;
        }

        Console.WriteLine($"Найдено изображений для обработки: {imageFiles.Count}");

        // Создаем или перезаписываем чистый файл text.txt перед стартом цикла
        File.WriteAllText(outputFile, $"--- Лог OCR обработки от {DateTime.Now} ---\n\n", Encoding.UTF8);

        // Настраиваем HttpClient (задаем большой таймаут, так как обработка картинок нейросетью требует времени)
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(10);

        int originalWidth = 0;
        int resized_width = 1280;
        // Цикл по всем найденным картинкам
        foreach (string imagePathe in imageFiles)
        {
            string fileName = Path.GetFileName(imagePathe);
            Console.WriteLine($"\n[Выполняется] Обработка файла: {fileName}...");

            try
            {
                // 1. Читаем картинку и кодируем её в строку Base64
                using (var someImg = Image.FromFile(imagePath))
                {
                    originalWidth = someImg.Width; // <-- Запоминаем изначальную ширину
                }
                
                byte[] imageBytes = ResizeImageIfNeeded(imagePathe, resized_width);
                File.WriteAllBytes("RESIZED_2.jpg", imageBytes);
                //byte[] imageBytesTransformed = ProcessImage(imageBytes);
                string b64String = Convert.ToBase64String(imageBytes);

                // 2. Формируем анонимный объект для JSON-тела запроса (копия структуры из Python)
                var payloadObject = new
                {
                    model = "qwen2.5vl:3b",
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = "Это скан технического экрана. Извлеки весь текст полностью, включая числа, параметры и единицы измерения. Не пропускай ничего.",
                            images = new[] { b64String }
                        }
                    },
                    stream = false,
                    options = new
                    {
                        num_predict = 4096,
                        num_ctx = 8192,
                        //temperature = 0.0, // Замораживаем точность
                        //top_p = 0.1
                    }
                };

                // Сериализуем объект в JSON-строку
                string jsonPayload = JsonSerializer.Serialize(payloadObject);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // 3. Отправляем HTTP POST запрос к Ollama
                var response = await client.PostAsync("http://localhost:11434/api/chat", content);
                response.EnsureSuccessStatusCode();

                // Читаем ответ сервера
                string responseString = await response.Content.ReadAsStringAsync();

                // 4. Десериализуем полученный JSON и достаем текст ответа модели
                using JsonDocument doc = JsonDocument.Parse(responseString);
                string rawContent = doc.RootElement
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                // 5. Формируем блок текста для сохранения
                StringBuilder fileBlock = new StringBuilder();
                fileBlock.AppendLine($"========================================");
                fileBlock.AppendLine($"ИМЯ ФАЙЛА: {fileName}");
                fileBlock.AppendLine($"ДАТА ОБРАБОТКИ: {DateTime.Now}");
                fileBlock.AppendLine($"========================================");
                fileBlock.AppendLine(rawContent);
                fileBlock.AppendLine("\n"); // Отступы между блоками разных картинок

                // Дописываем данные в файл text.txt
                File.AppendAllText(outputFile, fileBlock.ToString(), Encoding.UTF8);
                Console.WriteLine($"[Успех] Данные файла {fileName} добавлены в text.txt");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Ошибка] Не удалось обработать файл {fileName}: {ex.Message}");

                // Опционально: записываем ошибку по конкретному файлу в лог, чтобы не терять общую картину
                File.AppendAllText(outputFile, $"========================================\nИМЯ ФАЙЛА: {fileName}\n[ОШИБКА ОБРАБОТКИ]: {ex.Message}\n========================================\n\n", Encoding.UTF8);
            }
        }
        stopwatch.Stop();

        double totalSeconds = stopwatch.ElapsedMilliseconds / 1000.0;
        Console.WriteLine($"\n--- Статистика обработки ---");
        Console.WriteLine($"Ширина изначального фото: {resized_width} px");
        Console.WriteLine($"Потраченное время: {totalSeconds:F2} с");
        
        Console.WriteLine($"\nВсе готово! Все результаты собраны в файле: {outputFile}");
    }


    static byte[] ResizeImageIfNeeded(string imagePath, int maxDimension)
    {
        using (var originalImage = Image.FromFile(imagePath))
        {
            // Если изображение и так меньше лимита, просто отдаем его байты без пересчета
            if (originalImage.Width <= maxDimension && originalImage.Height <= maxDimension)
            {
                return File.ReadAllBytes(imagePath);
            }

            // Вычисляем новые пропорции
            int newWidth, newHeight;
            if (originalImage.Width > originalImage.Height)
            {
                newWidth = maxDimension;
                newHeight = (int)(originalImage.Height * ((double)maxDimension / originalImage.Width));
            }
            else
            {
                newHeight = maxDimension;
                newWidth = (int)(originalImage.Width * ((double)maxDimension / originalImage.Height));
            }

            // Создаем новый пустой холст нужного размера
            using (var resizedBitmap = new Bitmap(newWidth, newHeight))
            {
                // Настраиваем максимальное качество интерполяции при отрисовке
                using (var graphics = Graphics.FromImage(resizedBitmap))
                {
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;

                    // Рисуем старую картинку на новом холсте
                    graphics.DrawImage(originalImage, 0, 0, newWidth, newHeight);
                }

                // Сохраняем результат в поток байт как JPEG
                using (var ms = new MemoryStream())
                {
                    resizedBitmap.Save(ms, ImageFormat.Jpeg);
                    return ms.ToArray();
                }
            }
        }
    }

    static byte[] ProcessImage(byte[] imageBytes)
    {
        // 1. Загрузка изображения
        using var src = Cv2.ImDecode(imageBytes, ImreadModes.Color);





        // 2. Улучшение контраста (CLAHE) только для яркости
        using var claheImg = new Mat();
        using (var clahe = Cv2.CreateCLAHE(clipLimit: 3.0, tileGridSize: new OpenCvSharp.Size(8, 8)))
        {
            using var lab = new Mat();
            Cv2.CvtColor(src, lab, ColorConversionCodes.BGR2Lab);

            var channels = Cv2.Split(lab);
            using var lChannel = channels[0]; // Канал яркости
            using var aChannel = channels[1];
            using var bChannel = channels[2];




            clahe.Apply(lChannel, lChannel);

            using var newLab = new Mat();
            Cv2.Merge(new[] { lChannel, aChannel, bChannel }, newLab);
            Cv2.CvtColor(newLab, claheImg, ColorConversionCodes.Lab2BGR);
        }
        var bytes1 = claheImg.ToBytes(".jpg");

        // 3. Размытие и перевод в серый цвет
        using var blurred = new Mat();
        Cv2.GaussianBlur(claheImg, blurred, new OpenCvSharp.Size(5, 5), 2.0);
        //Cv2.MedianBlur(claheImg, blurred, 577);
        //Cv2.BilateralFilter(claheImg, blurred,15,150,150);

        using var gray = new Mat();
        Cv2.CvtColor(blurred, gray, ColorConversionCodes.BGR2GRAY);













        /*
        using var normalized = new Mat();
        var kernel = Cv2.GetStructuringElement(
            MorphShapes.Rect,
            new OpenCvSharp.Size(101, 101));

        Cv2.MorphologyEx(
            gray,
            background,
            MorphTypes.Close,
            kernel
         );
        Cv2.Absdiff(gray, background, normalized);
        */
        // Получаем карту освещения

        

        using var background = new Mat();

        Cv2.GaussianBlur(
            gray,
            background,
            new OpenCvSharp.Size(0, 0),
            50
        );
        // Вычитаем фон
        using var diffed = new Mat();
        Cv2.Absdiff(gray, background, diffed);



        //    using var normalized = new Mat();
        //Cv2.Normalize(diffed, normalized, 0, 255, NormTypes.MinMax);









        // 4. Ищем ЧЕРНЫЕ буквы (все, что темнее 51, станет черным)
        using var blackTextMask = new Mat();
        Cv2.Threshold(diffed, blackTextMask, 51, 255, ThresholdTypes.Binary);
        var bytes2 = diffed.ToBytes(".jpg");

        // 5. Ищем БЕЛЫЕ буквы (все, что светлее 204, станет черным после инверсии)
        using var whiteTextMask = new Mat();
        Cv2.Threshold(gray, whiteTextMask, 204, 255, ThresholdTypes.Binary);


        // 6. Объединяем буквы вместе (Черные буквы + Белые буквы)
        using var allTextMask = new Mat();
        Cv2.BitwiseOr(blackTextMask, whiteTextMask, allTextMask);

        // 7. Создаем финальный результат: черные буквы на белом фоне
        using var result = new Mat(src.Size(), MatType.CV_8UC1, new Scalar(255)); // Белый лист

        // Там, где был текст (allTextMask), красим в черный цвет (0)
        result.SetTo(new Scalar(0), allTextMask);

        Cv2.ImWrite("justCLAHE.jpg", claheImg);
        var bytes3 = result.ToBytes(".jpg");

        return bytes1;

    }
}