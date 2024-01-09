using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;

namespace PerfTestDLL
{

    public static class MemoryClass
    {
        public static List<byte[]> memoryLeakList = new List<byte[]>();

        public static string AddMemory(int mb=100)
        {
            for (int i = 0; i < mb; i++)
            {
                // Simulate adding 1 MB of data to the list
                byte[] data = new byte[1024 * 1024]; // 1 MB
                memoryLeakList.Add(data);

                Console.WriteLine($"Total memory allocated: {GC.GetTotalMemory(false) / (1024 * 1024)} MB");

                // Sleep for a short time to slow down memory consumption for demonstration
                System.Threading.Thread.Sleep(100);
            }

            return (GC.GetTotalMemory(false) / (1024 * 1024)).ToString();
        }
    }

    public class ServiceUpdater : IServiceUpdater
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger logger;
        private static SemaphoreSlim semaphore = new SemaphoreSlim(30); // Limit to 30 concurrent requests
        private static string endpoint = "https://" + Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME") + "/api/http2"; // "http://localhost:7151/api/http2";
        private static int tokenWaitTime = (int?)Convert.ToInt32(Environment.GetEnvironmentVariable("tokenWaitTime")) ?? 10;
        public static CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(tokenWaitTime));
        private static readonly object _lock = new object();

        public bool IsRunning { get; set; }

        public ServiceUpdater(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

            Console.WriteLine($"====ENDPOINT==== --> {endpoint}");

            if (Environment.GetEnvironmentVariable("localrun") == "true")
            {
                endpoint = "http://localhost:7260/api/http2";
            }
        }

        public async Task StartSender()
        {
            await Task.Delay(1);

            if(!IsRunning &&ServiceStatus.HardStatus)
            {
                ServiceStatus.Running = true;
                IsRunning = true;

                StartHttpSender();

                //await Task.Factory.StartNew(() =>
                //{
                //    StartHttpSender();
                //}, cancellationTokenSource.Token);
            }
        }

        public void StartHttpSender()
        {
            const int numThreads = 100;

            var tasks = new Task[numThreads];

            for (int i = 0; i < numThreads; i++)
            {
                tasks[i] = SendRequestsAsync(endpoint, _httpClientFactory);
            }

            Task.WhenAll(tasks);

            Console.WriteLine("All threads have completed.");
        }

        public async Task CancelHttpSender()
        {
            await Task.Delay(1);
            Console.WriteLine("Cancellation token has been cancelled.");
            
            ServiceStatus.Running = true;
            IsRunning = false;
        }

        public async Task SendRequestsAsync(string endpoint, IHttpClientFactory clientFactory)
        {
            await semaphore.WaitAsync(); // Wait for an open slot

            try
            {
                using (var httpClient = clientFactory.CreateClient())
                {
                    while (IsRunning)
                    {
                        int retryCount = 0;
                        bool requestSuccessful = false;

                        //while (!requestSuccessful)
                        //{
                            try
                            {
                                HttpResponseMessage response = await httpClient.GetAsync(endpoint);

                                if (response.IsSuccessStatusCode)
                                {
                                    Console.WriteLine($"Request successful. Thread: {Thread.CurrentThread.ManagedThreadId}");
                                    requestSuccessful = true;
                                }
                                else if ((int)response.StatusCode >= 500)
                                {
                                    Console.WriteLine($"Request failed with status code {response.StatusCode}. Thread: {Thread.CurrentThread.ManagedThreadId}, Retry Count: {retryCount}");
                                    retryCount++;
                                    await Task.Delay(3000); // Delay between retries (1 second)
                                }
                                else
                                {
                                    Console.WriteLine($"Request failed. Thread: {Thread.CurrentThread.ManagedThreadId}, Status Code: {response.StatusCode}");
                                    requestSuccessful = true; // Exit the loop if the status code is less than 500
                                }
                            }
                            catch (HttpRequestException ex)
                            {
                                Console.WriteLine($"Request And Retry Failed. Thread: {Thread.CurrentThread.ManagedThreadId}, Exception: {ex.Message}");
                                requestSuccessful = true; // Exit the loop if an exception occurs
                            }
                        //}

                        if (!ServiceStatus.Running)
                        {
                            break;
                        }

                        await Task.Delay(500); // Delay between requests (1 second)
                    }
                }
            }
            finally
            {
                semaphore.Release(); // Release the slot
            }
        }
    }

    public interface IThreadClass
    {
        public void StartHighThreadCount();
    }

    public class  ThreadClass : IThreadClass
    {
        private static readonly ManualResetEventSlim eventSlim = new ManualResetEventSlim(false);
        private static int threadCount = 600;

        public void StartHighThreadCount()
        {
            for (int i = 0; i < threadCount; i++)
            {
                Thread thread = new Thread(DoWork);
                thread.Start(i);
            }

            // Signal the event to start all the threads.
            eventSlim.Set();

            // Keep the main thread alive until all other threads are done.
            while (threadCount > 0)
            {
                Thread.Sleep(100);
            }

            eventSlim.Dispose();
        }

        public void DoWork(object data)
        {
            int threadNumber = (int)data;

            // Wait for the event to be signaled.
            eventSlim.Wait();

            Console.WriteLine($"Thread {threadNumber} is doing some work.");

            // Simulate some work.
            Thread.Sleep(600000);

            Interlocked.Decrement(ref threadCount);
        }
    }
}

public static class ServiceStatus
{
    public static bool Running = false;
    public static bool HardStatus = true;
}

public interface IServiceUpdater
{
    public bool IsRunning { get; set; }

    public Task StartSender();

    public void StartHttpSender();

    public Task CancelHttpSender();
}
