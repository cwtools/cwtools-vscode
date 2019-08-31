import dagre from 'dagre'
import cytoscape, { AnimateOptions, CenterOptions, CollectionElements } from 'cytoscape'
import cytoscapedagre from 'cytoscape-dagre'
import cytoscapecanvas from 'cytoscape-canvas'
import cytoscapeelk from 'cytoscape-elk'
import { link } from 'fs';
import popper from 'cytoscape-popper';
import tippy from 'tippy.js';
import mergeimages from 'merge-images'
declare module 'cytoscape' {
    interface CollectionElements {
        qtip(qtip: any): any;
        length: number;
    }
    interface Core {
        navigator(options: any): any;
        cyCanvas(): any;
    }

}


interface vscode {
    postMessage(message: any): void;
}

declare const acquireVsCodeApi : () => vscode;
const vscode : vscode = acquireVsCodeApi();

var labelMaxLength = 30;

function drawExtra(nodes : cytoscape.NodeCollection, ctx : OffscreenCanvasRenderingContext2D, zoom : number){
    // Draw shadows under nodes
    ctx.shadowColor = "black";
    ctx.shadowBlur = 25 * zoom;
    ctx.fillStyle = "#666";
    nodes.forEach((node: any) => {
        let text: string = node.data('entityType');
        const eventChars = text.split('_').map(f => f[0].toUpperCase()).join('');
        const eventChars2 = node.data('abbreviation') ? node.data('abbreviation') : eventChars;
        const eventChar = text[0].toUpperCase();
        const pos = node.position();

        ctx.fillStyle = node.data('isPrimary') ? "#EEE" : '#444';
        ctx.beginPath();
        ctx.arc(pos.x, pos.y, 15, 0, 2 * Math.PI, false);
        ctx.fill();
        ctx.fillStyle = "black";
        ctx.stroke();

        if (node.data('deadend_option')) {
            ctx.arc(pos.x, pos.y, 13, 0, 2 * Math.PI, false);
            ctx.stroke();
        }

        //Set text to black, center it and set font.
        ctx.fillStyle = "black";
        ctx.font = "16px sans-serif";
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";
        ctx.fillText(eventChars2, pos.x, pos.y);
    });
}

var _data: Array<any>;
var _options: Array<any>;
var _pretty: Array<any>;
var _cy: cytoscape.Core;
var _layer: any;
function tech(data : techNode [], nodes : Array<string>, edges : Array<any>){
    _data = data
    cytoscapedagre(cytoscape, dagre);
    cytoscapecanvas(cytoscape);
    cytoscape.use(cytoscapeelk)
    cytoscape.use(popper);
    var cy = cytoscape({
        container: document.getElementById('cy'),
        style: [ // the stylesheet for the graph
            {
                selector: 'node',
                style: {
                    'background-color': function(ele : any) { if (ele.data("isPrimary")) {return '#666'} else {return '#AAA' }},
                    'label': 'data(label)',
                    'color': function () { return document.getElementsByTagName("html")[0].style.getPropertyValue("--vscode-editor-foreground") }
                }
            },

            {
                selector: 'edge',
                style: {
                    'width': 3,
                    'line-color': '#ccc',
                    'mid-target-arrow-color': '#ccc',
                    'mid-target-arrow-shape': 'triangle',
                    'curve-style': 'haystack',
                    'line-style': function (ele: any) { if (ele.data("isPrimary")) { return 'solid' } else { return 'dashed' } }
                   // 'haystack-radius': 0.5
                }
            },
            {
                selector: 'node.highlight',
                style: {
                    'border-color': '#FFF',
                    'border-width': '2px'
                }
            },
            {
                selector: 'node.semitransp',
                style: { 'opacity': '0.5' }
            },
            {
                selector: 'edge.highlight',
                style: { 'mid-target-arrow-color': '#FFF' }
            },
            {
                selector: 'edge.semitransp',
                style: { 'opacity': '0.2' }
            }
        ],
        minZoom: 0.1,
        maxZoom: 5,
        layout: {
            name: 'preset',
            padding: 10
        },
        pixelRatio: 2
    })
    _cy = cy;
    var roots = [];
    console.log("nodes");

    var qtipname = function (text: string) { return { content: text, position: { my: 'top center', at: 'bottom center' }, style: { classes: 'qtip-bootstrap', tip: { width: 16, height: 8 } }, show: { event: 'mouseover' }, hide: { event: 'mouseout' } }; }

    console.log(nodes);
    data.forEach(function (element) {
        var node = cy.add({ group: 'nodes', data: {
            id: element.id,
            label: element.name || element.id,
            isPrimary: element.isPrimary,
            entityType: element.entityType,
            abbreviation: element.abbreviation,
            entityTypeDisplayName: element.entityTypeDisplayName
        }});
        let id = `<tr><td>id</td><td>${element.id}</td></tr>`
        let entityTypeDisplayName = element.entityTypeDisplayName ? `<tr><td colspan=2>${element.entityTypeDisplayName}</td></tr>` : ""
        let createRow = function (details : { key : string, values : string[]}) {
            let vals = details.values.join(", ")
            return `<tr><td>${details.key}</td><td>${vals}</td></tr>`
        }
        let detailsText = element.details ? element.details.map(createRow).join("") : ""
        let detailsTable =
            `<table>
            ${entityTypeDisplayName}
            ${id}
            ${detailsText}
            </table>`
        // let details = JSON.stringify(element.details)
        let ref = node.popperRef();
        let tip = tippy(ref, { // tippy options:
            content: () => {
                let content = document.createElement('div');

                content.innerHTML = detailsTable;

                return content;
            },
            sticky: true,
            flipOnUpdate: true
        });
        node.on('mouseover', () => tip.show());

    });
    data.forEach(function (element) {
        cy.nodes().filter((n : any) => n.id() == element.id).first()
                .data("location", element.location.filename)
    })
    console.log("edges");
    console.log(edges);
    edges.forEach(function (edge: any) {
        cy.add({ group: 'edges', data: { source: edge[0], target: edge[1] } })
    });
    data.forEach(function (element) {
        cy.edges().filter((n : any) => n.target().id() == element.id).forEach((e : any) => e.data("isPrimary", element.isPrimary));
    });
    console.log("fit");

    cy.fit();
    //var opts = { name: 'dagre', ranker: 'network-simplex', nodeDimensionsIncludeLabels: true };
    var opts = {
        name: 'elk',
        ranker: 'network-simplex',
        nodeDimensionsIncludeLabels: true,
        elk: {
            "elk.edgeRouting": "SPLINES",
            "elk.direction": "DOWN",
            "elk.aspectRatio": (cy.width() / cy.height()),
            "elk.algorithm": "layered",
            "elk.layered.nodePlacement.bk.edgeStraightening": "NONE",

            "elk.layered.compaction.connectedComponents": true
            // "elk.layered.unnecessaryBendpoints": true
            // "elk.disco.componentCompaction.strategy": "POLYOMINO",
            // "elk.layered.compaction.connectedComponents": "true",
            // "org.eclipse.elk.separateConnectedComponents": "false",
            //"org.eclipse.elk.layered.highDegreeNodes.treatment": "true"
            // "elk.layered.layering.nodePromotion.strategy": "NIKOLOV",
            // "elk.layered.layering.nodePromotion.maxIterations": 10
        }
    };
   // var layout = cy.layout(opts);
    //var opts = { name: }
    //var layout = cy.layout({ name: 'dagre', ranker: 'network-simplex' } );
    //layout.run();

    cy.fit();


   // layout.run();

    function flatten<T>(arr: Array<Array<T>>) {
        return arr.reduce(function (flat, toFlatten) {
            return flat.concat(toFlatten);
        }, []);
    }


    let toProcess = cy.elements();
    var groups: CollectionElements[] = [];

    var t: any = cy.elements();
    groups = t.components();
    var singles = groups.filter((f) => f.length === 1);
    var singles2: any = singles.reduce((p, c : any) => p.union(c), cy.collection())
    var rest = groups.filter((f) => f.length !== 1);

    var rest2 = rest.reduce((p, c : any) => p.union(c), cy.collection())

    var lrest: any = rest2.layout(opts);
    lrest.run();
    var bb = rest2.boundingBox({});
    var opts2 = { name: 'grid', condense: true, nodeDimensionsIncludeLabels: true }
    var lsingles: any = singles2.layout(opts2);
    lsingles.run();
    singles2.shift("y", (singles2.boundingBox({}).y2 + 10) * -1);
    cy.fit();

    // cy.on('select', 'node', function (_: any) {
    //     var node = cy.$('node:selected');
    //     if (node.nonempty()) {
    //         goToNode(node.data('id'));
    //     }
    // });

    /// Double click
    var tappedBefore : EventTarget;
    var tappedTimeout : NodeJS.Timer;

    cy.on('tap', function (event : Event) {
        var tappedNow :any = event.target;
        if (tappedTimeout && tappedBefore) {
            clearTimeout(tappedTimeout);
        }
        if (tappedBefore === tappedNow) {
            tappedNow.trigger('doubleTap', event);
            tappedBefore = null;
            //originalTapEvent = null;
        } else {
            tappedTimeout = setTimeout(function () { tappedBefore = null; }, 300);
            tappedBefore = tappedNow;
        }
    });
    cy.on('doubleTap', 'node', function (event : any) {
        goToNode(event.target.data('id'));
    });

    cy.on('mouseover', 'node', function (e) {
        var sel = e.target;
        cy.elements()
            .difference(sel.outgoers()
                .union(sel.incomers()))
            .not(sel)
            .addClass('semitransp');
        sel.addClass('highlight')
            .outgoers()
            .union(sel.incomers())
            .addClass('highlight');
    });
    cy.on('mouseout', 'node', function (e) {
        var sel = e.target;
        cy.elements()
            .removeClass('semitransp');
        sel.removeClass('highlight')
            .outgoers()
            .union(sel.incomers())
            .removeClass('highlight');
    });



    var layer = cy.cyCanvas({
        zIndex: 1,
        pixelRatio: "auto",
    });
    _layer = layer;
    var canvas = layer.getCanvas();
    var ctx = canvas.getContext('2d');

    cy.on("render", function (_: any) {
        layer.resetTransform(ctx);
        layer.clear(ctx);


        layer.setTransform(ctx);

        drawExtra(cy.nodes(), ctx, cy.zoom())
        // Draw shadows under nodes
        // ctx.shadowColor = "black";
        // ctx.shadowBlur = 25 * cy.zoom();
        // ctx.fillStyle = "#666";
        // cy.nodes().forEach((node: any) => {
        //     let text: string = node.data('entityType');
        //     const eventChars = text.split('_').map(f => f[0].toUpperCase()).join('');
        //     const eventChars2 = node.data('abbreviation') ? node.data('abbreviation') : eventChars;
        //     const eventChar = text[0].toUpperCase();
        //     const pos = node.position();

        //     ctx.fillStyle = node.data('isPrimary') ? "#EEE" : '#444';
        //     ctx.beginPath();
        //     ctx.arc(pos.x, pos.y, 15, 0, 2 * Math.PI, false);
        //     ctx.fill();
        //     ctx.fillStyle = "black";
        //     ctx.stroke();

        //     if (node.data('deadend_option')) {
        //         ctx.arc(pos.x, pos.y, 13, 0, 2 * Math.PI, false);
        //         ctx.stroke();
        //     }

        //     //Set text to black, center it and set font.
        //     ctx.fillStyle = "black";
        //     ctx.font = "16px sans-serif";
        //     ctx.textAlign = "center";
        //     ctx.textBaseline = "middle";
        //     ctx.fillText(eventChars2, pos.x, pos.y);
        // });
        //ctx.restore();
    });
    function debounce(func : any, wait : number, immediate : boolean) {
        // 'private' variable for instance
        // The returned function will be able to reference this due to closure.
        // Each call to the returned function will share this common timer.
        var timeout : NodeJS.Timer;

        // Calling debounce returns a new anonymous function
        return function () {
            // reference the context and args for the setTimeout function
            var context = this,
                args = arguments;

            // Should the function be called now? If immediate is true
            //   and not already in a timeout then the answer is: Yes
            var callNow = immediate && !timeout;

            // This is the basic debounce behaviour where you can call this
            //   function several times, but it will only execute once
            //   [before or after imposing a delay].
            //   Each time the returned function is called, the timer starts over.
            clearTimeout(timeout);

            // Set the new timeout
            timeout = setTimeout(function () {

                // Inside the timeout function, clear the timeout variable
                // which will let the next execution run when in 'immediate' mode
                timeout = null;

                // Check if the function already ran with the immediate flag
                if (!immediate) {
                    // Call the original function with apply
                    // apply lets you define the 'this' object as well as the arguments
                    //    (both captured before setTimeout)
                    func.apply(context, args);
                }
            }, wait);

            // Immediate mode and no wait timer? Execute the function..
            if (callNow) func.apply(context, args);
        }
    }
    function resizeme() {
        $("#cy").width(10);
        cy.resize();
        cy.center();
    }
    cy.on("resize", function (_: any) {
        debounce(resizeme, 10, false);
    });
    //$("#cy").width(10);
    //cy.resize();

    console.log("done");
    // var layer = cy.cyCanvas();
    // var canvas = layer.getCanvas();
    // var ctx = canvas.getContext('2d');

    // cy.on("resize", function (_) {
    //     $("#cy").width(10);
    //     cy.resize();
    //     cy.center();
    // });
}
// function main(data: Array<any>, triggers: any, options: any, pretties: Array<any>, eventComment: Array<any>, bundleEdges: boolean) {
//     var localised = new Map<string, string>();
//     var eventComments = new Map<string, string>(eventComment);
//     var getLoc = (key: string) => localised.has(key) ? localised.get(key) : key
//     var getName = (id: string) => eventComments.has(id) ? eventComments.get(id) == "" ? id : eventComments.get(id) : id
//     _data = data;
//     _options = options;
//     _pretty = pretties;
//     cyqtip(cytoscape, $);
//     cytoscapedagre(cytoscape, dagre);
//     cytoscapecanvas(cytoscape);

//     var cy = cytoscape({
//         container: document.getElementById('cy'),
//         style: [ // the stylesheet for the graph
//             {
//                 selector: 'node',
//                 style: {
//                     //'background-color': '#666',
//                     'label': 'data(label)'
//                 }
//             },

//             {
//                 selector: 'edge',
//                 style: {
//                     'width': 3,
//                     'line-color': '#ccc',
//                     'mid-target-arrow-color': '#ccc',
//                     'mid-target-arrow-shape': 'triangle',
//                     'curve-style': bundleEdges ? 'haystack' : 'bezier',
//                    // 'haystack-radius': 0.5
//                 }
//             }
//         ],
//         minZoom: 0.1,
//         maxZoom: 5,
//         layout: {
//             name: 'preset',
//             padding: 10
//         }
//     })

//     var roots = [];
//     var qtipname = function (text: string) { return { content: text, position: { my: 'top center', at: 'bottom center' }, style: { classes: 'qtip-bootstrap', tip: { width: 16, height: 8 } }, show: { event: 'mouseover' }, hide: { event: 'mouseout' } }; }


//     data.forEach(function (element: any) {
//         var name;

//         name = getName(element.ID);
//         var desc;
//         if (element.Desc === '') {
//             desc = element.ID;
//         }
//         else {
//             desc = getLoc(element.Desc);
//         }
//         var node : any = cy.add({ group: 'nodes', data: { id: element.ID, label: name, type: element.Key, hidden: element.Hidden } });
//         node.qtip(qtipname(desc));
//     });

//     triggers.forEach(function (event: any) {
//         var parentID = event[0];
//         event[1].forEach(function (immediates: any) {
//             immediates.forEach(function (target: any) {
//                 var childID = target;
//                 cy.add({ group: 'edges', data: { source: parentID, target: childID } })

//             })

//         })
//     });
//     options.forEach(function (event: any) {
//         var parentID = event[0];
//         event[1].forEach(function (option: any) {
//             var optionName = option[0][0] + "\n" + option[0][1];
//             option[1].forEach(function (target: any) {
//                 if (cy.getElementById(target).length > 0) {
//                     var edge : any = cy.add({ group: 'edges', data: { source: parentID, target: target } });
//                     if (optionName !== "") {
//                         edge[0].qtip(qtipname(optionName));
//                     }
//                 } else {
//                     cy.getElementById(parentID).data('deadend_option', true);
//                 }
//             })
//         })
//     })
//     cy.fit();
//     var opts = { name: 'dagre', ranker: 'network-simplex' };
//     //var opts = {name:'grid'};
//     //var layout = cy.layout(opts);
//     //var layout = cy.layout({ name: 'dagre', ranker: 'network-simplex' } );
//     //layout.run();
//     cy.fit();


//     //layout.run();
//     var layer = cy.cyCanvas();
//     var canvas = layer.getCanvas();
//     var ctx = canvas.getContext('2d');



//     function flatten<T>(arr: Array<Array<T>>) {
//         return arr.reduce(function (flat, toFlatten) {
//             return flat.concat(toFlatten);
//         }, []);
//     }


//     let toProcess = cy.elements();
//     var groups: CollectionElements[] = [];

//     var t: any = cy.elements();
//     groups = t.components();
//     var singles = groups.filter((f) => f.length === 1);
//     var singles2: any = singles.reduce((p, c : any) => p.union(c), cy.collection())
//     var rest = groups.filter((f) => f.length !== 1);

//     var rest2 = rest.reduce((p, c : any) => p.union(c), cy.collection())

//     var lrest: any = rest2.layout(opts);
//     lrest.run();
//     var bb = rest2.boundingBox({});
//     var opts2 = { name: 'grid', condense: true, nodeDimensionsIncludeLabels: true }
//     var lsingles: any = singles2.layout(opts2);
//     lsingles.run();
//     singles2.shift("y", (singles2.boundingBox({}).y2 + 10) * -1);
//     cy.fit();

//     cy.on("render", function (_ : any) {
//         layer.resetTransform(ctx);
//         layer.clear(ctx);


//         layer.setTransform(ctx);


//         // Draw shadows under nodes
//         ctx.shadowColor = "black";
//         ctx.shadowBlur = 25 * cy.zoom();
//         ctx.fillStyle = "#666";
//         cy.nodes().forEach((node : any) => {
//             let text: string = node.data('type');
//             const eventChars = text.split('_').map(f => f[0].toUpperCase()).join('');
//             const eventChar = text[0].toUpperCase();
//             const pos = node.position();

//             ctx.fillStyle = node.data('hidden') ? "#EEE" : '#888';
//             ctx.beginPath();
//             ctx.arc(pos.x, pos.y, 15, 0, 2 * Math.PI, false);
//             ctx.fill();
//             ctx.fillStyle = "black";
//             ctx.stroke();

//             if (node.data('deadend_option')) {
//                 ctx.arc(pos.x, pos.y, 13, 0, 2 * Math.PI, false);
//                 ctx.stroke();
//             }

//             //Set text to black, center it and set font.
//             ctx.fillStyle = "black";
//             ctx.font = "16px sans-serif";
//             ctx.textAlign = "center";
//             ctx.textBaseline = "middle";
//             ctx.fillText(eventChars, pos.x, pos.y);
//         });
//         ctx.restore();
//     });


//     var defaults = {
//         container: ".cy-row" // can be a HTML or jQuery element or jQuery selector
//         , viewLiveFramerate: 0 // set false to update graph pan only on drag end; set 0 to do it instantly; set a number (frames per second) to update not more than N times per second
//         , thumbnailEventFramerate: 30 // max thumbnail's updates per second triggered by graph updates
//         , thumbnailLiveFramerate: false // max thumbnail's updates per second. Set false to disable
//         , dblClickDelay: 200 // milliseconds
//         , removeCustomContainer: true // destroy the container specified by user on plugin destroy
//         , rerenderDelay: 100 // ms to throttle rerender updates to the panzoom for performance
//     };

//     //var nav = cy.navigator(defaults);


//     cy.on('select', 'edge', function (_ : any) {
//         var edges: cytoscape.EdgeCollection = cy.edges('edge:selected');
//         var edge: any = edges.first();
//         var opts : any = <AnimateOptions>{};
//         opts.zoom = cy.zoom();
//         opts.center = <CenterOptions>{ eles: edge };
//         cy.animate(opts);
//     });

//     cy.on("resize", function (_ : any) {
//         $("#cy").width(10);
//         cy.resize();
//         cy.center();
//     });
// }

export function goToNode(id : string) {
    var node = _data.filter(x => x.id === id)[0];
    var uri = node.location.filename
    var line = node.location.line
    var column = node.location.column
    vscode.postMessage({"command": "goToFile", "uri": uri, "line": line, "column": column})
}

export function exportImage() {

    var png = _cy.png({ full: true, output: 'base64uri' });
    var boundingBox = _cy.elements().boundingBox({includeLabels:true})
    const canvas = new OffscreenCanvas(Math.ceil(boundingBox.x2 - boundingBox.x1) * 2, Math.ceil(boundingBox.y2 - boundingBox.y1) * 2)
    console.log(canvas.width);
    console.log(_cy.elements().boundingBox({}).x1)
    console.log(_cy.elements().boundingBox({}).x2)
    const ctx = canvas.getContext("2d") as OffscreenCanvasRenderingContext2D
    ctx.scale(2, 2)
    ctx.translate(-1 * boundingBox.x1, -1 * boundingBox.y1)
    // _layer.resetTransform(ctx);
    // _layer.clear(ctx);
    drawExtra(_cy.nodes(), ctx, 0.5)
    canvas.convertToBlob({"type":"png"}).then((canvasImage) =>
        {
            var reader = new FileReader()
            reader.onloadend = (function () {
                var res = {
                    src: reader.result as string,
                    x: 0,// boundingBox.x1 * -1,
                    y: 0//boundingBox.y1 * -1
                }
                //res = res.substr(res.indexOf(',') + 1)
                // vscode.postMessage({ "command": "saveImage", "image": png })
                // vscode.postMessage({ "command": "saveImage", "image": res })
                mergeimages([png, res]).then((final) => vscode.postMessage({ "command": "saveImage", "image": final.substr(final.indexOf(',') + 1) }))
            })
            reader.readAsDataURL(canvasImage);
            // mergeimages([png, canvasImage])
        }
    )
}


interface Location
{
    filename : string
    line : number
    column : number
}
interface techNode
{
    name : string
    prereqs : Array<string>
    references : Array<string>
    id : string
    location: Location
    isPrimary : boolean
    details? : Array<{ key: string, values : Array<string> }>
    entityTypeDisplayName? : string
    abbreviation? : string
}


export function go(nodesJ: any) {
    //console.log(nodesJ);
    var nodes: Array<techNode> = nodesJ//JSON.parse(nodesJ);
    //console.log(nodes);
    var nodes2 = nodes.map((a) => a.id);
    var edges = nodes.map((a) => a.references.map((b) => [a.id, b]));
    var edges2 : string[][]= [].concat(...edges)
    var nodes3 = edges2.map(a => a[1])
    let nodes4 : string[] = [].concat(nodes2, nodes3);
    // console.log(nodes2);
    // console.log(edges2);
    //document.getElementById('detailsTarget')!.innerHTML = "Parsing data...";
    //tech(["a", "b", "c", "d"], [["a", "b"],["c","d"]]);
    var nodesfin = new Set(nodes4)
    var edgesfin = new Set(edges2)
    tech(nodes, [...nodesfin], [...edgesfin]);
}

window.addEventListener('message', event => {

    const message = event.data; // The JSON data our extension sent

    switch (message.command) {
        case 'go':
            go(message.data)
            break;
        case 'exportImage':
            exportImage();
            break;
    }
});

//go("test");
