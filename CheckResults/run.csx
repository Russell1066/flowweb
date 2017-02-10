#r "Microsoft.WindowsAzure.Storage"

using System.Net;
using Microsoft.WindowsAzure.Storage.Table;

public class TraceIdEntity : TableEntity
{
    public string TraceId { get; set; }
    public bool Accepted { get; set; }
    public string Results { get; set; }
    public bool Viewed { get; set; }
}

public static HttpResponseMessage Run(HttpRequestMessage req, string traceId, CloudTable traceIds, TraceWriter log)
{
    log.Info($"C# HTTP trigger function processed request {traceId}");

    var result = traceIds.Execute(TableOperation.Retrieve<TraceIdEntity>("0", traceId));
    var found = result.Result as TraceIdEntity;

    if (found == null)
    {
        return req.CreateResponse(HttpStatusCode.Accepted);
    }

    // Update table?
    try
    {
        log.Info("updating as viewed");
        found.Viewed = true;
        traceIds.Execute(TableOperation.Merge(found));
    }
    catch (Exception ex)
    {
        log.Error(ex.ToString());
    }

    return req.CreateResponse(HttpStatusCode.OK);
}