#r "Newtonsoft.Json"
#r "..\bin\FlowCore.dll"
#r "Microsoft.WindowsAzure.Storage"
#load "..\Common\common.csx"

using System;
using System.Net;
using System.Diagnostics;
using System.Threading;
using Newtonsoft.Json;
using Microsoft.WindowsAzure.Storage.Table;

using SolverCore;

public class SolverTable : TableEntity
{
    public string TraceId { get; set; }
    public int SolutionId { get; set; }
    public int SolutionCount { get; set; }
    public string StartTime { get; set; }
    public string EndTime { get; set; }
    public bool Completed { get; set; }
    public bool IsSolution { get; set; }

    public SolverTable() { }

    public SolverTable(string traceId, int solutionId)
    {
        PartitionKey = traceId;
        RowKey = solutionId.ToString();
        TraceId = traceId;
        SolutionId = solutionId;
    }
}

public static void Run(string myQueueItem, CloudTable solverTable,
    CloudTable traceIds,
    CancellationToken token,
    TraceWriter log)
{
    var board = new FlowBoard();
    var solver = SolverMgr.GetSolver(myQueueItem, board);
    var logger = new Logger(log, solver.Wrapper.TracingId);
    var traceId = solver.Wrapper.TracingId;

    // If this has already been done - don't do it again
    var retrieve = solverTable.Execute(TableOperation.Retrieve<SolverTable>(solver.Wrapper.TracingId, solver.Wrapper.SolutionId));
    var results = retrieve.Result as SolverTable;
    if (results != null)
    {
        logger.Info($"Already run to completion {results.SolutionId} solution was {results.IsSolution}");
        return;   
    }

    SolverTable solverTableItem = new SolverTable(traceId, Int32.Parse(solver.Wrapper.SolutionId))
    {
        SolutionCount = Int32.Parse(solver.Wrapper.SolutionSetId),
        StartTime = DateTime.Now.ToString("o"),
    };

    logger.Info($"Solving {solver.Wrapper.SolutionId} of {solver.Wrapper.SolutionSetId} : Start");

    var task = solver.Solver(token);
    bool done = false;
    var sleepLogger = Task.Run(() =>
    {
        for (;!done;)
        {
            for(int i=0; i < 20 && !done;++i)
            {
                Task.Delay(1000, token).Wait();
            }

            if(!done)
            {
                logger.Info("...Solving is hard...");
            }
        }
    });
    Task.WaitAny(task, sleepLogger);
    done = true;

    solverTableItem.IsSolution = task.Result; 
    logger.Info($"IsSolution : {solverTableItem.IsSolution}");
    solverTableItem.EndTime = DateTime.Now.ToString("o");
    solverTableItem.Completed = true;
    logger.Info("");
    logger.Info($"Writing {solver.Wrapper.SolutionId} of {solver.Wrapper.SolutionSetId} : {solverTableItem.IsSolution}");
    logger.Info($"PartitionKey = {solverTableItem.PartitionKey}");
    logger.Info($"RowKey = {solverTableItem.RowKey}");
    logger.Info($"TraceId = {solverTableItem.TraceId}");
    logger.Info($"SolutionId = {solverTableItem.SolutionId}");
    logger.Info($"SolutionCount = {solverTableItem.SolutionId}");
    logger.Info($"StartTime = {solverTableItem.StartTime}");
    logger.Info($"EndTime = {solverTableItem.EndTime}");
    logger.Info($"Completed = {solverTableItem.Completed}");
    logger.Info($"IsSolution = {solverTableItem.IsSolution}");
    solverTable.ExecuteAsync(TableOperation.Insert(solverTableItem)).Wait();
    logger.Info($"Wrote {solver.Wrapper.SolutionId} of {solver.Wrapper.SolutionSetId} : {solverTableItem.IsSolution}");
}