﻿namespace Eventful.Raven

open Eventful

open Raven.Abstractions.Commands
open Raven.Json.Linq

type BatchWrite = seq<ProcessAction>

module BatchOperations =
    let log = Common.Logging.LogManager.GetLogger(typeof<BatchWrite>)
    let buildPutCommand (documentStore : Raven.Client.IDocumentStore) (writeRequest:DocumentWriteRequest) =
        let cmd = new PutCommandData()
        cmd.Document <- RavenJObject.FromObject(writeRequest.Document, documentStore.Conventions.CreateSerializer())
        cmd.Key <- writeRequest.DocumentKey
        cmd.Etag <- writeRequest.Etag
        cmd.Metadata <- writeRequest.Metadata.Force()
        cmd

    let buildDeleteCommand (deleteRequest:DocumentDeleteRequest) =
        let cmd = new DeleteCommandData()
        cmd.Key <- deleteRequest.DocumentKey
        cmd.Etag <- deleteRequest.Etag
        cmd
        
    let buildCommandFromProcessAction documentStore processAction =
        match processAction with
        | Write (x,request) -> buildPutCommand documentStore x :> ICommandData
        | Delete (x,_) -> buildDeleteCommand x :> ICommandData
        
    let writeBatch (documentStore : Raven.Client.IDocumentStore) database (docs:seq<BatchWrite>) = async {
        let buildCmd = (buildCommandFromProcessAction documentStore)
        try 
            let! batchResult = 
                docs
                |> Seq.collect (Seq.map buildCmd)
                |> Array.ofSeq
                |> documentStore.AsyncDatabaseCommands.ForDatabase(database).BatchAsync
                |> Async.AwaitTask

            return Some (batchResult, docs)
        with    
            | :? System.AggregateException as e -> 
                log.Error("Write Error", e)
                log.Error("Write Inner", e.InnerException)
                return None
            | e ->
                log.Error("Write Error", e)
                return None
    }

    let bulkInsert (documentStore : Raven.Client.IDocumentStore) (inserter : Raven.Client.Document.BulkInsertOperation) (docs:seq<BatchWrite>) = async {
        let buildCmd = (buildCommandFromProcessAction documentStore)
        try 
            for doc in docs do
                for req in doc do
                    match req with
                    | Write (req, _) ->
                        inserter.Store(RavenJObject.FromObject(req.Document, documentStore.Conventions.CreateSerializer()), req.Metadata.Force(), req.DocumentKey)
                    | _ -> ()
        with    
            | :? System.AggregateException as e -> 
                log.Error("Write Error", e)
                log.Error("Write Inner", e.InnerException)
            | e ->
                log.Error("Write Error", e)
    }