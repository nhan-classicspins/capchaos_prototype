using System;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace SheetSync
{
    // Downloads the sheet as CSV text. Plain Task (no UniTask), resolved on the main thread by
    // UnityWebRequest's own completed callback, so it is safe to await from game code.
    public static class SheetCsvFetcher
    {
        public static Task<string> FetchAsync(string url, int timeoutSeconds)
        {
            var completion = new TaskCompletionSource<string>();
            var request = UnityWebRequest.Get(url);
            request.timeout = timeoutSeconds;

            request.SendWebRequest().completed += _ =>
            {
                try
                {
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        completion.SetResult(request.downloadHandler.text);
                    }
                    else
                    {
                        completion.SetException(new Exception($"Sheet fetch failed ({request.result}): {request.error} [{url}]"));
                    }
                }
                finally
                {
                    request.Dispose();
                }
            };

            return completion.Task;
        }
    }
}
