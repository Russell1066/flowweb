#r "Newtonsoft.Json"
#r "..\bin\FlowCore.dll"

#load "..\Common\common.csx"

#r "Microsoft.WindowsAzure.Storage"
using Newtonsoft.Json;

using System.Net;
using Microsoft.WindowsAzure.Storage.Table;

using SolverCore;


public static async Task<HttpResponseMessage> Run(HttpRequestMessage req,
    IAsyncCollector<string> outputQueueItem, CloudTable traceIds, TraceWriter log)
{
    log.Info("Launching");
    var userResults = new UploadResults()
    {
    };

    var logger = new Logger(log, userResults.TraceId);

    logger.Info($"Processing request");

    // parse query parameter
    string name = req.GetQueryNameValuePairs()
        .FirstOrDefault(q => string.Compare(q.Key, "name", true) == 0)
        .Value;

    if (name == null)
    {
        logger.Error($"no name found");
        return req.CreateResponse(HttpStatusCode.BadRequest, new BadRequest()
        {
            TraceId = userResults.TraceId,
            FailureType = "Missing required element",
            Description = "No name found"
        });
    }

    userResults.Name = name;

    // Get request body
    FlowBoard.BoardDefinition board = null;
    try
    {
        board = await req.Content.ReadAsAsync<FlowBoard.BoardDefinition>();
        userResults.Board = JsonConvert.SerializeObject(board);
    }
    catch (Exception)
    {
        logger.Error($"Bad format - failed conversion");
        return req.CreateResponse(HttpStatusCode.BadRequest, new BadRequest()
        {
            TraceId = userResults.TraceId,
            FailureType = "Bad format",
            Description = "Must submit a valid board"
        });
    }

    try
    {
        logger.Info($"Found a board of square size {board.BoardSize} and  {board.EndPointList.Count} items");
        foreach (var endPoint in board.EndPointList)
        {
            logger.Info($"color = {endPoint.FlowColor}, point1 = ({endPoint.Pt1.X}, {endPoint.Pt1.Y}) point2 = ({endPoint.Pt2.X}, {endPoint.Pt2.Y})");
        }

        if (!board.IsValid())
        {
            logger.Error($"Bad format - {board.DescribeFailures()}");
            return req.CreateResponse(HttpStatusCode.BadRequest, new BadRequest()
            {
                TraceId = userResults.TraceId,
                FailureType = "bad format",
                Description = board.DescribeFailures()
            });
        }

        logger.Info($"Writing to table");
        await traceIds.ExecuteAsync(TableOperation.Insert(userResults));
        logger.Info($"Writing to queue");
        await outputQueueItem.AddAsync(userResults.TraceId);
        logger.Info($"Success");
        return req.CreateResponse(HttpStatusCode.Accepted, new RequestResponse(userResults));
    }
    catch (Exception ex)
    {
        logger.Error(ex.ToString());
        return req.CreateResponse(HttpStatusCode.BadRequest, new BadRequest()
        {
            TraceId = userResults.TraceId,
            FailureType = "processing failed",
        });
    }
}