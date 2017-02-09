#r "Newtonsoft.Json"
#r "..\bin\FlowCore.dll"
#load "..\Common\common.csx"

using System.Net;
using Newtonsoft.Json;

using SolverCore;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, IAsyncCollector<string> outputQueueItem, TraceWriter log)
{
    log.Info("C# HTTP trigger function processed a request.");

    // parse query parameter
    string name = req.GetQueryNameValuePairs()
        .FirstOrDefault(q => string.Compare(q.Key, "name", true) == 0)
        .Value;

    // Get request body
    FlowBoard.BoardDefinition board = null;
    try
    {
        board = await req.Content.ReadAsAsync<FlowBoard.BoardDefinition>();
    }
    catch (Exception)
    {
        return req.CreateResponse(HttpStatusCode.BadRequest, "Must submit a valid board - bad format");
    }

    if (name == null)
    {
        return req.CreateResponse(HttpStatusCode.BadRequest, "Cannot upload without a name");
    }

    try
    {
        log.Info($"Found a board of square size {board.BoardSize}");
        log.Info($"Found a board with {board.EndPointList.Count} items");
        foreach (var endPoint in board.EndPointList)
        {
            log.Info($"color = {endPoint.FlowColor}, point1 = ({endPoint.Pt1.X}, {endPoint.Pt1.Y}) point2 = ({endPoint.Pt2.X}, {endPoint.Pt2.Y})");
        }

        if (!board.IsValid())
        {
            return req.CreateResponse(HttpStatusCode.BadRequest, $"Board contains invalid elements\r\n{board.DescribeFailures()}");
        }

        var wrapper = new BoardWrapper()
        {
            Name = name,
            Board = board,
        };
        var json = JsonConvert.SerializeObject(wrapper);
        await outputQueueItem.AddAsync(json);
        return req.CreateResponse(HttpStatusCode.Accepted, wrapper);
    }
    catch (Exception ex)
    {
        log.Error(ex.ToString());
        return req.CreateResponse(HttpStatusCode.BadRequest, $"{name} processing failed");
    }
}