﻿module Dynamics.Crm.SolutionExchanger

open System
open System.Net
open System.IO
open System.ServiceModel.Description
open Microsoft.Xrm.Sdk
open Microsoft.Xrm.Sdk.Query
open Microsoft.Xrm.Sdk.Client
open Microsoft.Xrm.Sdk.Discovery
open Microsoft.Crm.Sdk.Messages
open Microsoft.Xrm.Tooling.Connector
open System.Web.Services.Description

let internal _timeOutDefaults = new TimeSpan(0, 10, 0)

type CrmEndpointParams =
    {
        ConnectionString: string
        TimeOut: int option
    }

let CrmEndpointDefaults = 
    {
        ConnectionString = ""
        TimeOut = Some 10
    }

/// Query all solutions in system
let internal RetrieveAllSolutions (service : IOrganizationService) =
    let query = 
        new QueryExpression ( 
            ColumnSet = new ColumnSet(true), 
            Criteria = new FilterExpression (FilterOperator = LogicalOperator.And),
            EntityName = "solution")

    query.Criteria.AddCondition(new ConditionExpression("ismanaged", ConditionOperator.Equal, false))
    // Skip "default" solution
    query.Criteria.AddCondition(new ConditionExpression("solutionid", ConditionOperator.NotEqual, Guid("FD140AAF-4DF4-11DD-BD17-0019B9312238")))
    // Skip "active" solution
    query.Criteria.AddCondition(new ConditionExpression("solutionid", ConditionOperator.NotEqual, Guid("FD140AAE-4DF4-11DD-BD17-0019B9312238")))
    // Skip "Basic" solution
    query.Criteria.AddCondition(new ConditionExpression("solutionid", ConditionOperator.NotEqual, Guid("25a01723-9f63-4449-a3e0-046cc23a2902")))
    
    try
        Some(service.RetrieveMultiple(query))
    with 
    | ex -> printf "Encountered exception during retrieval of solutions: %s" ex.Message
            None
  
/// Creates Organization Service for communicating with Dynamics CRM
/// ## Parameters
///
///  - `username` - Username for authentication
///  - `password` - Password for authentication
///  - `url` - URL that is used to connect to CRM
let private CreateOrganizationService (serviceParams: CrmEndpointParams) =
    printfn "Creating Organization Service: %A" serviceParams.ConnectionString
    
    try
        let conn = new CrmServiceClient(serviceParams.ConnectionString);

        if not conn.IsReady then
            failwith (sprintf "Could not connect, Error: %s" conn.LastCrmError)

        if conn.OrganizationWebProxyClient <> null then
                conn.OrganizationWebProxyClient :> IOrganizationService;
        else
                conn.OrganizationServiceProxy :> IOrganizationService;
    with   
    | ex -> failwith (sprintf "Error while creating organization service: %A" ex.Message)

/// Publishes all solution component changes.
/// ## Parameters
///
///  - `organizationService` - Organization Service to use for executing requests
let PublishAll crmEndpoint =
    printfn "Publishing all"
    let serviceParams = crmEndpoint CrmEndpointDefaults
    let organizationService = CreateOrganizationService serviceParams
    
    let publishRequest = new PublishAllXmlRequest()
    let response = organizationService.Execute(publishRequest)
    printfn "Successfully published all" 

/// Writes solution byte[] to file. If a file with the same name is present in given path, it is being overridden.
/// ## Parameters
///
///  - `fileName` - File name for file that is created
///  - `solution` - Solution as byte[] that was retrieved from ExportSolution
///  - `path` - Path to write file to, be sure to pass with trailing backslash
let WriteSolutionToFile fileName solution path = 
    printfn "Writing solution to file %A" (Path.Combine(path, fileName))
    if not (Directory.Exists path) then
        printfn "Destination path %s does not exist, creating directories\n" path
        Directory.CreateDirectory(path) |> ignore
        printfn "Successfully created path"
    let filePath = Path.Combine(path, fileName)
    File.WriteAllBytes(filePath, solution)
    printfn "Successfully wrote solution to file"
            
/// Exports solution from Dynamics CRM and stores it in memory as byte[]
/// ## Parameters
///
///  - `organizationService` - Organization Service to use for executing requests
///  - `solutionName` - Unique name of solution that should be exported
///  - `managed` - Boolean: True for exporting as managed solution, false for exporting as unmanaged
let ExportSolution crmEndpoint solutionName (managed : bool) =
    printfn "Exporting solution %A" (solutionName + ": " + if managed then "Managed" else "Unmanaged")
    
    let serviceParams = crmEndpoint CrmEndpointDefaults
    let organizationService = CreateOrganizationService serviceParams
        
    let exportSolutionRequest = new ExportSolutionRequest( Managed = managed, SolutionName = solutionName )
    
    try
        let response = organizationService.Execute(exportSolutionRequest) :?> ExportSolutionResponse
        printfn "Successfully exported solution"
        Some(response.ExportSolutionFile)
    with
    | ex -> printfn "Exception occured! Message: %s, Stack Trace: %s" ex.Message ex.StackTrace
            None
                

/// Comment
let ExportAllSolutions crmEndpoint managed =
    printfn "Retrieving all unmanaged solutions in Organization"
    
    let serviceParams = crmEndpoint CrmEndpointDefaults
    let organizationService = CreateOrganizationService serviceParams
        
    let solutions = (RetrieveAllSolutions organizationService)

    if solutions.IsNone then
        failwith (sprintf "Failed to retrieve solutions for organization %s\n" serviceParams.ConnectionString)

    solutions.Value.Entities
    |> Seq.map (fun solution -> 
        try
            printf "Found solution %s, ID: %s\n" (solution.GetAttributeValue<string>("uniquename")) (solution.GetAttributeValue<Guid>("solutionid").ToString())
            Some (ExportSolution crmEndpoint (solution.GetAttributeValue<string>("uniquename")) managed, solution.GetAttributeValue<string>("uniquename"))
        with
        | ex -> printf "Encountered Exception: %s\n" ex.Message
                None)
    |> Seq.filter(fun solution -> solution.IsSome)
    |> Seq.map(fun solution -> solution.Value)

/// Imports zipped solution file to Dynamics CRM
/// ## Parameters
///
///  - `organizationService` - Organization Service to use for executing requests
///  - `path` - Full path to zipped solution file
let ImportSolution crmEndpoint path =
    printfn "Importing solution %A" path
    let serviceParams = crmEndpoint CrmEndpointDefaults
    let organizationService = CreateOrganizationService serviceParams
    
    if not (File.Exists(path)) then
        failwith (sprintf "File at path %A does not exist!" path)
    let file = File.ReadAllBytes(path)
    let importSolutionRequest = new ImportSolutionRequest( CustomizationFile = file, PublishWorkflows = true )
    let response = organizationService.Execute(importSolutionRequest) :?> ImportSolutionResponse
    printfn "Successfully imported solution"
