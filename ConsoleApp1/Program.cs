using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        // Путь к папке с картинками и файл для записи результатов
        string folderPath = @"C:\Users\k_alejnikov\Pictures\takes";
        string outputFile = Path.Combine(AppContext.BaseDirectory, "text.txt");

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

        // Цикл по всем найденным картинкам
        foreach (string imagePath in imageFiles)
        {
            string fileName = Path.GetFileName(imagePath);
            Console.WriteLine($"\n[Выполняется] Обработка файла: {fileName}...");

            try
            {
                // 1. Читаем картинку и кодируем её в строку Base64
                byte[] imageBytes = ResizeImageIfNeeded(imagePath, 1280);
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
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

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
}