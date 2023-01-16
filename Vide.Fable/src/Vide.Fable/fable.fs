[<AutoOpen>]
module Vide.Fable

open System.Runtime.CompilerServices
open Browser
open Browser.Types
open Vide
open System

type FableContext
    (
        parent: Node, 
        evaluateView: FableContext -> unit
    ) =
    inherit VideContext()
    
    let mutable keptChildren = []
    let keepChild child = keptChildren <- (child :> Node) :: keptChildren
    let appendToParent child = parent.appendChild(child) |> ignore

    override this.RequestEvaluation() = evaluateView this
    member internal _.EvaluateView = evaluateView
    member _.Parent = parent
    member _.AddElement<'n when 'n :> HTMLElement>(tagName: string) =
        let elem = document.createElement tagName 
        do elem |> keepChild 
        do elem |> appendToParent 
        elem :?> 'n
    member _.AddText(text: string) =
        let elem = document.createTextNode text 
        do elem |> keepChild
        do elem |> appendToParent
        elem
    member _.KeepChild(child: Node) =
        child |> keepChild |> ignore
    member _.GetObsoleteChildren() =
        let childNodes =
            let nodes = parent.childNodes
            [ for i in 0 .. nodes.length-1 do nodes.Item i ]
        childNodes |> List.except keptChildren

type BuilderOperations = | Clear

let inline text text =
    Vide <| fun s (ctx: FableContext) ->
        let createNode () = ctx.AddText(text)
        let node =
            match s with
            | None -> createNode ()
            | Some (node: Text) ->
                if typeof<Text>.IsInstanceOfType(node) then
                    if node.textContent <> text then
                        node.textContent <- text
                    ctx.KeepChild(node)
                    node
                else
                    createNode ()
        (), Some node

type Modifier<'n> = 'n -> unit
type NodeBuilderState<'n,'s> = option<'n> * option<'s>
type ChildAction = Keep | DiscardAndCreateNew

module BuilderHelper =
    let createNode elemName (ctx: FableContext) =
        ctx.AddElement<'n>(elemName)
    let checkOrUpdateNode elemName (node: #Node) =
        match node.nodeName.Equals(elemName, StringComparison.OrdinalIgnoreCase) with
        | true -> Keep
        | false ->
            // TODO:
            console.log($"TODO: if/else detection? Expected node name: {elemName}, but was: {node.nodeName}")
            DiscardAndCreateNew
    let runBuilder
        createNode
        checkOrUpdateNode
        initModifiers
        evalModifiers
        (Vide childVide: Vide<'v1,'fs,FableContext>)
        resultSelector
        : Vide<'v2, NodeBuilderState<'n,'fs>, FableContext>
        =
        Vide <| fun s (ctx: FableContext) ->
            Debug.print 0 "RUN:NodeBuilder"
            let inline runModifiers modifiers node =
                for m in modifiers do m node
            let s,cs = separateStatePair s
            let node,cs =
                // Can it happen that s is Some and cs is None? I don't think so.
                // But: See comment in definition of: Vide.Core.Vide
                match s with
                | None ->
                    let newNode,s = createNode ctx,cs
                    do runModifiers initModifiers newNode
                    newNode,s
                | Some node ->
                    match checkOrUpdateNode node with
                    | Keep ->
                        ctx.KeepChild(node)
                        node,cs
                    | DiscardAndCreateNew ->
                        createNode ctx,None
            do runModifiers evalModifiers node
            let childCtx = FableContext(node, ctx.EvaluateView)
            let cv,cs = childVide cs childCtx
            for x in childCtx.GetObsoleteChildren() do
                node.removeChild(x) |> ignore
                // we don'tneed this? Weak enough?
                // events.RemoveListener(node)
            resultSelector node cv, Some (Some node, cs)

type NodeBuilder<'n when 'n :> Node>
    (
        createNode: FableContext -> 'n,
        checkOrUpdateNode: 'n -> ChildAction
    ) =
    inherit VideBuilder()
    
    member _.CreateNode = createNode
    member _.CheckOrUpdateNode = checkOrUpdateNode
    
    member val InitModifiers: Modifier<'n> list = [] with get,set
    member val EvalModifiers: Modifier<'n> list = [] with get,set

    member this.DoRun(childVide, resultSelector) =
        BuilderHelper.runBuilder
            createNode 
            checkOrUpdateNode 
            this.InitModifiers 
            this.EvalModifiers 
            childVide 
            resultSelector

module Yield =
    let inline yieldNodeBuilder (nb: #NodeBuilder<'n>) =
        nb { () }
    let inline yieldText (s: string) =
        text s
    let inline yieldBuilderOp (op: BuilderOperations) =
        Vide <| fun s (ctx: FableContext) ->
            match op with
            | Clear -> ctx.Parent.textContent <- ""
            (),None

type VideComponentBuilder() =
    inherit VideBuilder()
    member _.Yield(nb) = Yield.yieldNodeBuilder nb
    member _.Yield(s) = Yield.yieldText s
    member _.Yield(op) = Yield.yieldBuilderOp op

type VoidBuilder<'v,'n when 'n :> Node>(createNode, checkOrUpdateNode, resultSelector: 'n -> 'v) =
    inherit NodeBuilder<'n>(createNode, checkOrUpdateNode)
    member this.Run(childVide) =
        this.DoRun(childVide, fun node _ -> resultSelector node)

type ContentBuilder<'n when 'n :> Node>(createNode, checkOrUpdateNode) =
    inherit NodeBuilder<'n>(createNode, checkOrUpdateNode)
    member _.Yield(nb) = Yield.yieldNodeBuilder nb
    member _.Yield(s) = Yield.yieldText s
    member _.Yield(op) = Yield.yieldBuilderOp op
    member this.Run(childVide) =
        this.DoRun(childVide, fun node childRes -> childRes)

type VideBuilder with
    member this.Bind(m: VoidBuilder<'v,'n>, f) =
        this.Bind(m {()}, f)

type HTMLVoidElementBuilder<'v,'n when 'n :> HTMLElement>(elemName, resultSelector) =
    inherit VoidBuilder<'v,'n>(
        BuilderHelper.createNode elemName,
        BuilderHelper.checkOrUpdateNode elemName,
        resultSelector)

type HTMLContentElementBuilder<'n when 'n :> HTMLElement>(elemName) =
    inherit ContentBuilder<'n>(
        BuilderHelper.createNode elemName,
        BuilderHelper.checkOrUpdateNode elemName)

[<Extension>]
type NodeBuilderExtensions =
    // TODO: Switch back to non-inheritance API

    /// Called once on initialization.
    [<Extension>]
    static member OnInit(this: #NodeBuilder<_>, m: Modifier<_>) =
        do this.InitModifiers <- m :: this.InitModifiers
        this
    
    /// Called on every Vide evaluatiopn cycle.
    [<Extension>]
    static member OnEval(this: #NodeBuilder<_>, m: Modifier<_>) =
        do this.EvalModifiers <- m :: this.EvalModifiers
        this

module App =
    let inline doCreate appCtor (host: #Node) (content: Vide<'v,'s,FableContext>) onEvaluated =
        let content = 
            // TODO: Really a ContentBuilder? Why?
            ContentBuilder((fun _ -> host), fun _ -> Keep) { content }
        let ctxCtor = fun eval -> FableContext(host, eval)
        appCtor content ctxCtor onEvaluated
    let createFable host content onEvaluated =
        doCreate App.create host content onEvaluated
    let createFableWithObjState host content onEvaluated =
        doCreate App.createWithObjState host content onEvaluated

let vide = VideComponentBuilder()
