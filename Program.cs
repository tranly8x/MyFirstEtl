﻿using System;
using System.Threading.Tasks;
using Paillave.Etl.Core;
using Paillave.Etl.FileSystem;
using Paillave.Etl.Zip;
using Paillave.Etl.TextFile;
using Paillave.Etl.SqlServer;
using System.Data.SqlClient;

namespace SimpleTutorial
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var processRunner = StreamProcessRunner.Create<string>(DefineProcess);
            processRunner.DebugNodeStream += (sender, e) => { /* PLACE A CONDITIONAL BREAKPOINT HERE FOR DEBUG */ };
            using (var cnx = new SqlConnection(args[1]))
            {
                cnx.Open();
                var executionOptions = new ExecutionOptions<string>
                {
                    Resolver = new SimpleDependencyResolver().Register(cnx),
                    TraceProcessDefinition = DefineTraceProcess,
                    // UseDetailedTraces = true // activate only if per row traces are meant to be caught
                };
                var res = await processRunner.ExecuteAsync(args[0], executionOptions);
                Console.Write(res.Failed ? "Failed" : "Succeeded");
                if (res.Failed)
                    Console.Write($"{res.ErrorTraceEvent.NodeName}({res.ErrorTraceEvent.NodeTypeName}):{res.ErrorTraceEvent.Content.Message}");
            }
        }
        private static void DefineProcess(ISingleStream<string> contextStream)
        {
            // TODO: define your ELT process here
        }
        private static void DefineTraceProcess(IStream<TraceEvent> traceStream, ISingleStream<string> contentStream)
        {
            traceStream
              .Where("keep only summary of node and errors", i => i.Content is CounterSummaryStreamTraceContent || i.Content is UnhandledExceptionStreamTraceContent)
              .Select("create log entry", i => new ExecutionLog
              {
                  DateTime = i.DateTime,
                  ExecutionId = i.ExecutionId,
                  EventType = i.Content switch
                  {
                      CounterSummaryStreamTraceContent => "EndOfNode",
                      UnhandledExceptionStreamTraceContent => "Error",
                      _ => "Unknown"
                  },
                  Message = i.Content switch
                  {
                      CounterSummaryStreamTraceContent counterSummary => $"{i.NodeName}: {counterSummary.Counter}",
                      UnhandledExceptionStreamTraceContent unhandledException => $"{i.NodeName}({i.NodeTypeName}): [{unhandledException.Level.ToString()}] {unhandledException.Message}",
                      _ => "Unknown"
                  }
              })
              .SqlServerSave("save traces", o => o.ToTable("dbo.ExecutionTrace"));
        }
        private class ExecutionLog
        {
            public DateTime DateTime { get; set; }
            public Guid ExecutionId { get; set; }
            public string EventType { get; set; }
            public string Message { get; set; }
        }
    }
}