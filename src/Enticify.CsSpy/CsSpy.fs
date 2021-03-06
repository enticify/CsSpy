﻿namespace Enticify.CsSpy

module DictionaryVisualizer =
    open Microsoft.VisualStudio.DebuggerVisualizers
    open System.Windows.Forms
    open System.Diagnostics
    open Microsoft.CommerceServer.Runtime
    open System.Runtime.Serialization.Formatters.Binary

    ///The tree type...
    type 'T Tree =
        | Node of 'T * list<'T Tree>
        | Leaf of 'T

    ///Maps a CS dictionary to our Tree type.
    let mapObjectToTree item =
        let rec inner (src:obj) desc = 
            match src with
            | :? IDictionary as dic ->
                let children =  
                    [   for key in dic do
                        yield inner dic.[key.ToString()] (key.ToString()) ]
                let text = desc + "<IDictionary>"
                if children.Length = 0 then Leaf(text + "=empty") else Node(text, children)
            | :? ISimpleList as sl -> 
                let children =  
                    [   for i in 0..sl.Count-1 do
                        yield inner sl.[i] (i.ToString())  ]
                let text = desc + "<ISimpleList>"
                if children.Length = 0 then Leaf(text + "=empty") else Node(text, children)
            //Enumerate enumerables (but not strings or we get chars in tree view).
            | :? System.Collections.IEnumerable as items when items.GetType() <> typeof<System.String> -> 
                let children =  
                    items 
                    |> Seq.cast<obj>
                    |> Seq.mapi (fun i item -> inner item (i.ToString()))
                    |> List.ofSeq
                let text = desc + "<" + items.GetType().Name + ">"
                if children.Length = 0 then Leaf(text + "=empty") else Node(text, children)
            | value -> 
                let sValue =
                    try
                        value.ToString()
                    with
                    | ex -> sprintf "Value fail: %s" ex.Message
                Leaf(desc + "<" + value.GetType().Name + ">=" + sValue) 
        inner item ""

    ///The type that serializes the source data for debuggee to debugger transport.
    type TreeViewObjectSource() = 
        inherit VisualizerObjectSource() 
        override this.GetData(target : obj, outgoingData : System.IO.Stream) = 
            let formatter = BinaryFormatter() 
            match target with
            | :? DictionaryClass | :? SimpleListClass as item -> 
                let data = mapObjectToTree item 
                formatter.Serialize(outgoingData, data) 
            | null -> failwith "Visualizer target object is null."//Should not happen.
            | x -> failwith (sprintf "Visualizer target object type %s not supported." (x.GetType().Name))//Should not happen.
            ()

    ///The type that visualizes our data.
    type DictionaryVisualizerDialog() =
        inherit DialogDebuggerVisualizer()
        override x.Show(dvs:IDialogVisualizerService, vop:IVisualizerObjectProvider) = 
            //Get out lovely tree data...
            let treeData = 
                try
                    let data = vop.GetObject()
                    match data with
                    | :? Tree<string> as tree -> tree
                    | _ -> Leaf("No data.")
                with
                | ex -> Leaf(sprintf "CsSpy #FAIL - %s" ex.Message)
            //Create TreeNode hierarchy
            let nodeTree = 
                let rec buildTreeView (node:TreeNode) (tree:Tree<string>) = 
                    match tree with 
                    | Leaf(text) -> 
                        node.Nodes.Add(text) |> ignore
                    | Node(text, nodeList) -> 
                        let childNode = new TreeNode(text)
                        node.Nodes.Add(childNode) |> ignore
                        for node in nodeList do
                            buildTreeView childNode node 
                let rootNode = new TreeNode()
                buildTreeView rootNode treeData
                rootNode.Nodes.[0]//discard the root node as it is noise.
            //Bang it in to windows forms.
            let tv = new TreeView(Dock = DockStyle.Fill)
            tv.Nodes.Add(nodeTree) |> ignore
            nodeTree.Expand()
            let form = new Form()
            form.Width <- 1024 
            form.Height <- 768 
            //form.Opacity <- 0.83
            form.Controls.Add(tv)
            form.Text <- "Enticify CsSpy Visualizer"
            form.Icon <- new System.Drawing.Icon(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("windows-7-tip.ico"))
            dvs.ShowDialog(form) |> ignore
            ()

    [<assembly:DebuggerVisualizer(typeof<DictionaryVisualizerDialog>, 
                                  typeof<TreeViewObjectSource>, 
                                  Target = typedefof<DictionaryClass>, 
                                  Description = "CsSpy DictionaryClass Visualizer")>]
    [<assembly:DebuggerVisualizer(typeof<DictionaryVisualizerDialog>, 
                                  typeof<TreeViewObjectSource>, 
                                  Target = typedefof<SimpleListClass>, 
                                  Description = "CsSpy SimpleListClass Visualizer")>]
    do()