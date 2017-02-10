#r "Newtonsoft.Json"
#r "Microsoft.WindowsAzure.Storage"

using System.Net;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, string traceId, CloudTable traceIds, TraceWriter log)
{
    log.Info($"C# HTTP trigger function processed request {traceId}");

    var result = await traceIds.ExecuteAsync(TableOperation.Retrieve<UploadResults>("0", traceId));
    var found = result.Result as UploadResults;

    if (found == null)
    {
        return req.CreateResponse(HttpStatusCode.Accepted);
    }

    // Update table
    log.Info("updating as viewed");
    found.Viewed = true;
    await traceIds.ExecuteAsync(TableOperation.Merge(found));

    return req.CreateResponse(HttpStatusCode.OK, JsonConvert.SerializeObject(found));
}