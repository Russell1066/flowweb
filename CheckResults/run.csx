#r "Microsoft.WindowsAzure.Storage"

#load "..\Common\common.csx"

using System.Net;
using Microsoft.WindowsAzure.Storage.Table;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, string traceId, CloudTable traceIds, TraceWriter log)
{
    log.Info($"C# HTTP trigger function processed request {traceId}");
    var logger = new Logger(log, traceId);

    var result = await traceIds.ExecuteAsync(TableOperation.Retrieve<UploadResults>(UploadResults.PARTITIONKEY, traceId));
    var found = result.Result as UploadResults;

    if (found == null)
    {
        logger.Info("TraceId not found in id table");
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    // Update table
    if(found.Processed)
    {
        logger.Info("updating as viewed");
        found.Viewed = true;
        await traceIds.ExecuteAsync(TableOperation.Merge(found));
    }

    return req.CreateResponse(found.Processed ? HttpStatusCode.OK : HttpStatusCode.Accepted, new RequestResponse(found));
}