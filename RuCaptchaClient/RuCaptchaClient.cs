using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace mevoronin.RuCaptchaNETClient
{
    /// <summary>
    /// Клиент сервиса RuCaptcha
    /// </summary>
    public class RuCaptchaClient
    {
        private readonly string _apiKey;
        const string Host = "http://rucaptcha.com";
        static readonly Dictionary<string, string> Errors = new Dictionary<string, string>
        {
            {
                "CAPCHA_NOT_READY",
                "Капча в работе, ещё не расшифрована, необходимо повтороить запрос через несколько секунд."
            },
            {"ERROR_WRONG_ID_FORMAT", "Неверный формат ID капчи. ID должен содержать только цифры."},
            {"ERROR_WRONG_CAPTCHA_ID", "Неверное значение ID капчи."},
            {
                "ERROR_CAPTCHA_UNSOLVABLE",
                "Капчу не смогли разгадать 3 разных работника. Средства за эту капчу не списываются."
            },
            {"ERROR_WRONG_USER_KEY", "Не верный формат параметра key, должно быть 32 символа."},
            {"ERROR_KEY_DOES_NOT_EXIST", "Использован несуществующий key."},
            {"ERROR_ZERO_BALANCE", "Баланс Вашего аккаунта нулевой."},
            {
                "ERROR_NO_SLOT_AVAILABLE",
                "Текущая ставка распознования выше, чем максимально установленная в настройках Вашего аккаунта."
            },
            {"ERROR_ZERO_CAPTCHA_FILESIZE", "Размер капчи меньше 100 Байт."},
            {"ERROR_TOO_BIG_CAPTCHA_FILESIZE", "Размер капчи более 100 КБайт."},
            {
                "ERROR_WRONG_FILE_EXTENSION",
                "Ваша капча имеет неверное расширение, допустимые расширения jpg,jpeg,gif,png."
            },
            {"ERROR_IMAGE_TYPE_NOT_SUPPORTED", "Сервер не может определить тип файла капчи."},
            {
                "ERROR_IP_NOT_ALLOWED",
                "В Вашем аккаунте настроено ограничения по IP с которых можно делать запросы. И IP, с которого пришёл данный запрос не входит в список разрешённых."
            }
        };

        /// <summary>
        /// Конструктор
        /// </summary>
        /// <param name="apiKey">Ключ доступа к API</param>
        public RuCaptchaClient(string apiKey)
        {
            this._apiKey = apiKey;
        }


        public async Task<string> SolveCapthca(Uri fileUrl, TimeSpan? timeout = null ,CaptchaConfig config = null)
        {
            TimeSpan timeSpan = timeout ?? TimeSpan.FromSeconds(60);
            var captchaId = await UploadCaptchaFile(fileUrl, config);
            var startTime = DateTime.UtcNow;


            while (true)
            {
                await Task.Delay(3000);

                try
                {
                    return await GetCaptcha(captchaId);
                }
                catch (Exception e)
                {
                    if (startTime.Add(timeSpan) < DateTime.UtcNow)
                    {
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Получить расшифрованное значение капчи
        /// </summary>
        /// <param name="captchaId">Id капчи</param>
        /// <returns></returns>
        public async Task<string> GetCaptcha(string captchaId)
        {
            return await MakeGetRequest($"{Host}/res.php?key={_apiKey}&action=get&id={captchaId}");
        }

        /// <summary>
        /// Загрузить файл капчи
        /// </summary>
        /// <param name="fileName">Путь к файлу с капчей</param>
        /// <param name="config">Параметры</param>
        /// <returns></returns>
        public async Task<string> UploadCaptchaFile(string fileName, CaptchaConfig config = null)
        {
            byte[] imageBytes;
            using (FileStream fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read))
            {
                imageBytes = new byte[fileStream.Length];
                await fileStream.ReadAsync(imageBytes, 0, (int) fileStream.Length);
            }

            return await UploadCaptchaFile(fileName, imageBytes, config);
        }

        /// <summary>
        /// Загрузить файл капчи
        /// </summary>
        /// <param name="fileUrl">Путь к файлу с капчей</param>
        /// <param name="config">Параметры</param>
        /// <returns></returns>
        public async Task<string> UploadCaptchaFile(Uri fileUrl, CaptchaConfig config = null)
        {
            using (var webClient = new WebClient())
            {
                byte[] imageBytes = await webClient.DownloadDataTaskAsync(fileUrl);
                return await UploadCaptchaFile(Guid.NewGuid().ToString().Replace("-", ""), imageBytes, config);
            }
        }

        private async Task<string> UploadCaptchaFile(string fileName, byte[] imageBytes, CaptchaConfig config)
        {
            string url = $"{Host}/in.php";
            string boundary = "---------------------------" + DateTime.Now.Ticks.ToString("x");
            byte[] boundarybytes = Encoding.ASCII.GetBytes("\r\n--" + boundary + "\r\n");

            HttpWebRequest request = (HttpWebRequest) WebRequest.Create(url);
            request.ContentType = "multipart/form-data; boundary=" + boundary;
            request.Method = "POST";
            request.KeepAlive = true;
            request.Credentials = CredentialCache.DefaultCredentials;

            using (Stream requestStream = request.GetRequestStream())
            {
                WriteKeys(requestStream, config, boundarybytes);
                WriteHeader(requestStream, fileName);
                requestStream.Write(imageBytes, 0, imageBytes.Length);


                byte[] trailer = Encoding.ASCII.GetBytes("\r\n--" + boundary + "--\r\n");
                requestStream.Write(trailer, 0, trailer.Length);
            }

            using (WebResponse response = await request.GetResponseAsync())
            {
                Stream responseStream = response.GetResponseStream();
                StreamReader responseReader = new StreamReader(responseStream);
                return ParseAnswer(await responseReader.ReadToEndAsync());
            }
        }

        private static void WriteHeader(Stream requestStream, string fileName)
        {
            string headerTemplate =
                "Content-Disposition: form-data; name=\"{0}\"; filename=\"{1}\"\r\nContent-Type: {2}\r\n\r\n";
            string header = string.Format(headerTemplate, "file", fileName, "image/jpeg");
            byte[] headerbytes = Encoding.UTF8.GetBytes(header);
            requestStream.Write(headerbytes, 0, headerbytes.Length);
        }

        private void WriteKeys(Stream requestStream, CaptchaConfig config, byte[] boundarybytes)
        {
            string formdataTemplate = "Content-Disposition: form-data; name=\"{0}\"\r\n\r\n{1}";

            NameValueCollection nvc = new NameValueCollection {{"key", _apiKey}};
            if (config != null)
                nvc.Add(config.GetParameters());

            foreach (string key in nvc.Keys)
            {
                requestStream.Write(boundarybytes, 0, boundarybytes.Length);
                string formitem = string.Format(formdataTemplate, key, nvc[key]);
                byte[] formitembytes = Encoding.UTF8.GetBytes(formitem);
                requestStream.Write(formitembytes, 0, formitembytes.Length);
            }
            requestStream.Write(boundarybytes, 0, boundarybytes.Length);
        }

        /// <summary>
        /// Получить текущий баланс аккаунта
        /// </summary>
        /// <returns></returns>
        public async Task<decimal> GetBalance()
        {
            string url = $"{Host}/res.php?key={_apiKey}&action=getbalance";
            string stringBalance = await MakeGetRequest(url);
            decimal balance = decimal.Parse(stringBalance, CultureInfo.InvariantCulture.NumberFormat);
            return balance;
        }

        /// <summary>
        /// Выполнение Get запроса по указанному URL
        /// </summary>
        private async Task<string> MakeGetRequest(string url)
        {
            var client = new WebClient{Encoding = Encoding.UTF8};
            var serviceAnswer = await client.DownloadStringTaskAsync(url);
            return ParseAnswer(serviceAnswer);
        }

        /// <summary>
        /// Разбор ответа
        /// </summary>
        private string ParseAnswer(string serviceAnswer)
        {
            if (Errors.Keys.Contains(serviceAnswer))
                throw new RuCaptchaException($"{Errors[serviceAnswer]} ({serviceAnswer})");
            else if (serviceAnswer.StartsWith("OK|"))
                return serviceAnswer.Substring(3);
            else
                return serviceAnswer;

        }
    }
}
