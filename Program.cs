using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PerfTestDLL
{
    public class PerfClass
    {
        private static readonly ManualResetEventSlim eventSlim = new ManualResetEventSlim(false);
        private static int threadCount = 600;
        //private readonly IHttpClientFactory _httpClientFactory;
        private static string endpoint = "https://" + Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME") + "/api/http2";
        private static SemaphoreSlim semaphore = new SemaphoreSlim(30);

        public void StartHighThreadCount()
        {
            for (int i = 0; i < threadCount; i++)
            {
                new Thread(DoWork).Start(i);
            }
            eventSlim.Set();
            while (threadCount > 0)
            {
                Thread.Sleep(100);
            }
            eventSlim.Dispose();
        }

        public void DoWork(object data)
        {
            int value = (int)data;
            eventSlim.Wait();
            Console.WriteLine($"Thread {value} is doing some work.");
            Thread.Sleep(600000);
            Interlocked.Decrement(ref threadCount);
        }

        public static class ServiceStatus
        {
            public static bool Running = true;
        }

        public static async Task SendRequestsAsync(string endpoint, CancellationToken cancellationToken, IHttpClientFactory clientFactory)
        {
            await semaphore.WaitAsync();
            try
            {
                using HttpClient httpClient = clientFactory.CreateClient();
                while (!cancellationToken.IsCancellationRequested)
                {
                    int retryCount = 0;
                    bool requestSuccessful = false;
                    while (!requestSuccessful && retryCount < 100)
                    {
                        try
                        {
                            HttpResponseMessage httpResponseMessage = await httpClient.GetAsync(endpoint, cancellationToken);
                            if (httpResponseMessage.IsSuccessStatusCode)
                            {
                                Console.WriteLine($"Request successful. Thread: {Thread.CurrentThread.ManagedThreadId}");
                                requestSuccessful = true;
                            }
                            else if (httpResponseMessage.StatusCode >= HttpStatusCode.InternalServerError)
                            {
                                Console.WriteLine($"Request failed with status code {httpResponseMessage.StatusCode}. Thread: {Thread.CurrentThread.ManagedThreadId}, Retry Count: {retryCount}");
                                retryCount++;
                                await Task.Delay(3000);
                            }
                            else
                            {
                                Console.WriteLine($"Request failed. Thread: {Thread.CurrentThread.ManagedThreadId}, Status Code: {httpResponseMessage.StatusCode}");
                                requestSuccessful = true;
                            }
                        }
                        catch (HttpRequestException ex)
                        {
                            Console.WriteLine($"Request And Retry Failed. Thread: {Thread.CurrentThread.ManagedThreadId}, Exception: {ex.Message}");
                            requestSuccessful = true;
                        }
                    }
                    await Task.Delay(500);
                }
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}
