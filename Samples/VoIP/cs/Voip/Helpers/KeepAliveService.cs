using System;
using System.Threading.Tasks;
using VoipTasks;
using VoipTasks.BackgroundOperations;
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Background;
using Windows.Foundation.Collections;

namespace Voip.Helpers
{
    static class KeepAliveService
    {
        public static bool IsKeepAliveTask(IBackgroundTaskInstance taskInstance)
        {
            var details = taskInstance.TriggerDetails as AppServiceTriggerDetails;
            return details != null && details.Name == BackgroundOperation.KeepAliveServiceTaskName;
        }

        public static async void RunKeepAliveTask(IBackgroundTaskInstance taskInstance)
        {
            var instanceId = taskInstance.InstanceId;
 
            void TaskCancellationHandler(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
            {
                Log.WriteLine($"KeepAlive > Windows requested cancel for Task ID={instanceId}, Reason={reason}");
            }

            BackgroundTaskDeferral deferral = null;
            try
            {
                deferral = taskInstance.GetDeferral();

                Log.WriteLine($"KeepAlive > Activating Task ID={instanceId}");

                taskInstance.Canceled += TaskCancellationHandler;

                var details = (AppServiceTriggerDetails) taskInstance.TriggerDetails;
                using (var connection = details.AppServiceConnection)
                {
                    var firstRequest = (await connection.ListenFirstRequestAsTask()).Request;
                    await firstRequest.SendResponseAsync(new ValueSet());
                    Log.WriteLine($"KeepAlive > Sent response for Task ID={instanceId}");
                }
            }
            finally
            {
                Log.WriteLine($"KeepAlive > Completing Task ID={instanceId}");
                taskInstance.Canceled -= TaskCancellationHandler;
                deferral?.Complete();
            }
        }

        static Task<AppServiceRequestReceivedEventArgs> ListenFirstRequestAsTask(this AppServiceConnection connection)
        {
            var firstRequestTcs = new TaskCompletionSource<AppServiceRequestReceivedEventArgs>();

            void RequestCallback(AppServiceConnection sender, AppServiceRequestReceivedEventArgs args)
            {
                // remove our handler after the first request
                connection.RequestReceived -= RequestCallback;
                firstRequestTcs.SetResult(args);
            }

            // attach our handler
            connection.RequestReceived += RequestCallback;

            return firstRequestTcs.Task;
        }
    }
}
