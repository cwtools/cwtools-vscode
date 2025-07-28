import * as cyM from 'cytoscape';
import { CollectionReturnValue, EventObject, StylesheetJsonBlock } from 'cytoscape'
import { registerCytoscapeCanvas } from './canvas'
import cytoscapeelk from 'cytoscape-elk'
import popper from 'cytoscape-popper';
import tippy, { Props, type Instance } from 'tippy.js';
import mergeimages from 'merge-images'

declare module 'cytoscape' {
    interface Core {
        cyCanvas(options: { pixelRatio: string; zIndex: number }): {
            getCanvas(): HTMLCanvasElement;
            clear: (ctx: CanvasRenderingContext2D) => void;
            resetTransform(ctx: CanvasRenderingContext2D): void;
            setTransform: (ctx: CanvasRenderingContext2D) => void;
        };
    }

}


interface vscode {
    postMessage(message: unknown): void;
}

declare const acquireVsCodeApi : () => vscode;
const vscode : vscode = acquireVsCodeApi();

function drawExtra(nodes : cytoscape.NodeCollection, ctx : CanvasRenderingContext2D, zoom : number){
    // Draw shadows under nodes
    ctx.shadowColor = "black";
    ctx.shadowBlur = 25 * zoom;
    ctx.fillStyle = "#666";
    nodes.forEach((node) => {
        const text: string = node.data('entityType');
        const eventChars = text.split('_').map(f => f[0].toUpperCase()).join('');
        const eventChars2 = node.data('abbreviation') ? node.data('abbreviation') : eventChars;
        const pos = node.position();

        ctx.fillStyle = node.data('isPrimary') ? "#EEE" : '#444';
        ctx.globalAlpha = node.hasClass('semitransp') ? 0.5 : 1;
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

const style : StylesheetJsonBlock[] = [ // the stylesheet for the graph
    {
        selector: 'node',
        style: {
            'background-color': function (ele) { if (ele.data("isPrimary")) { return '#666' } else { return '#AAA' } },
            'label': 'data(label)',
            'color': function () { return document.getElementsByTagName("html")[0].style.getPropertyValue("--vscode-editor-foreground") },
            'text-background-color': function () { return document.getElementsByTagName("html")[0].style.getPropertyValue("--vscode-editor-background") },
            'text-background-opacity': 0.8,
            'text-wrap': "wrap",
            'text-max-width':"200px"

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
            'line-style': function (ele) { if (ele.data("isPrimary")) { return 'solid' } else { return 'dashed' } }
            // 'haystack-radius': 0.5
        }
    },
    {
        selector: 'edge[label]',
        style: {
            'label': 'data(label)',
            'color': function () { return document.getElementsByTagName("html")[0].style.getPropertyValue("--vscode-editor-foreground") },
            'text-background-color': function () { return document.getElementsByTagName("html")[0].style.getPropertyValue("--vscode-editor-background") },
            'text-background-opacity': 0.8,
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
        style: { 'opacity': 0.5 }
    },
    {
        selector: 'edge.highlight',
        style: { 'mid-target-arrow-color': '#FFF' }
    },
    {
        selector: 'edge.semitransp',
        style: { 'opacity': 0.2 }
    }
]
let _cy: cytoscape.Core;
function tech(data: techNode[], edges: Array<EdgeInput>, settings : settings,json?: { elements: { nodes?: cytoscape.ElementDefinition[]; edges?: cytoscape.ElementDefinition[]; } | cytoscape.ElementDefinition[]; } & Record<string, unknown>){
    const importingJson = json !== undefined;
    // Rest of your imports

    // Then when calling the function:
    registerCytoscapeCanvas(cyM.default());
    cyM.default.use(cytoscapeelk)
    cyM.default.use(popper);
    const cy = cyM.default({
        container: document.getElementById('cy'),
        minZoom: 0.1,
        maxZoom: 5,
        layout: {
            name: 'preset',
            padding: 10
        },
        pixelRatio: 1,
        wheelSensitivity: settings.wheelSensitivity
    })
    // }
    _cy = cy;

    const layer = cy.cyCanvas({
        zIndex: 1,
        pixelRatio: "auto",
    });
    const canvas = layer.getCanvas();
    const ctx = canvas.getContext('2d');

    console.log("nodes");


    //console.log(nodes);

    /// Initial setup
    if (!importingJson){

        data.forEach(function (element) {
            cy.add({ group: 'nodes', data: {
                id: element.id,
                label: element.name || element.id,
                isPrimary: element.isPrimary,
                entityType: element.entityType,
                abbreviation: element.abbreviation,
                entityTypeDisplayName: element.entityTypeDisplayName ? element.entityTypeDisplayName : element.entityType,
                details: element.details,
                location: element.location
            }});
        });
        const allIDs = data.map((el) => el.id);
        edges.forEach(function (edge) {
            if (allIDs.includes(edge.source) && allIDs.includes(edge.target)){
                cy.add({ group: 'edges', data: { source: edge.source, target: edge.target, label: edge.label } }).data("isPrimary", true)
            }
        });
        data.forEach(function (element) {
            if(element.isPrimary == false){
                cy.edges().filter((n) => n.target().id() == element.id || n.source().id() == element.id).forEach((e) => {e.data("isPrimary", false);});
            }
        });

    }
    else {
        cy.json(json);
    }
    cy.style(style)
    /// Tooltips
    cy.nodes().forEach(function(node) {

        const simpleTooltip = `<strong>${node.data("entityTypeDisplayName")}</strong>: ${node.data("id")}`
        const createRow = function (details : { key : string, values : string[]}) {
            const vals = details.values.join(", ")
            return `<tr><td>${details.key}</td><td>${vals}</td></tr>`
        }
        const detailsText = node.data("details") ? node.data("details").map(createRow).join("") : ""
        const detailsTable =
            `${simpleTooltip}
            <table class="cwtools-table">
            ${detailsText ? detailsText : "<tr><td class=\"cwtools-text-center\">-</td></tr>"}
            </table>`
        const ref = node.popperRef();
        let isSimple = true;
        const simpleOptions : Partial<Props> = {
            content: () => {
                const content = document.createElement('div');

                content.innerHTML = simpleTooltip;

                return content;
            },
            onHidden: (() => {}),
            sticky: true,
            // flipOnUpdate: true,
            trigger: "manual",
            delay: [null, 200]
        }
        let hoverTimeout : NodeJS.Timeout;
        const complexOptions = {
            content: () => {
                const content = document.createElement('div');

                content.innerHTML = detailsTable;

                return content;
            },
            onHidden: (instance: Instance) =>
            {
                clearTimeout(hoverTimeout)
                instance.setProps(simpleOptions)
                isSimple = true
            },
            sticky: true,
            flipOnUpdate: true,
            interactive: true,
            trigger: "manual"

        }
        const tip = tippy(ref.contextElement, simpleOptions);
        const expandTooltip = function(element : Instance) {
            element.setProps(complexOptions);
            isSimple = false
        }
        node.on('mouseover', () => {
            tip.show();
            hoverTimeout = setTimeout(expandTooltip, 1000, tip);
        });
        node.on('mouseout', () =>
        {
            clearTimeout(hoverTimeout)
            if(isSimple) {
                tip.hide()
            }
        });

    });

    /// Layout
    if(!importingJson){
        console.log("fit");

        cy.fit();
        //var opts = { name: 'dagre', ranker: 'network-simplex', nodeDimensionsIncludeLabels: true };
        const opts = {
            name: 'elk',
            //ranker: 'network-simplex',
            nodeDimensionsIncludeLabels: true,
            elk: {
                "elk.edgeRouting": "SPLINES",
                "elk.direction": "DOWN",
                "elk.aspectRatio": (cy.width() / cy.height()),
                "elk.algorithm": "layered",
                "elk.layered.nodePlacement.bk.edgeStraightening": "NONE",

                "elk.layered.compaction.connectedComponents": true,
                "elk.hierarchyHandling": "SEPARATE_CHILDREN",
                // "elk.layered.unnecessaryBendpoints": true
                // "elk.disco.componentCompaction.strategy": "POLYOMINO",
                // "elk.layered.compaction.connectedComponents": "true",
                // "org.eclipse.elk.separateConnectedComponents": "false",
                //"org.eclipse.elk.layered.highDegreeNodes.treatment": "true"
                // "elk.layered.layering.nodePromotion.strategy": "NIKOLOV",
                // "elk.layered.layering.nodePromotion.maxIterations": 10
            }
        };


        cy.fit();


        let groups: CollectionReturnValue[] = [];

        const t = cy.elements();
        groups = t.components();
        const singles = groups.filter((f) => f.length === 1);
        const singles2 = singles.reduce((p, c) => p.union(c), cy.collection())
        const rest = groups.filter((f) => f.length !== 1);

        const rest2 = rest.reduce((p, c) => p.union(c), cy.collection())

        const lrest = rest2.layout(opts);
        lrest.run();
        const opts2 = { name: 'grid', condense: true, nodeDimensionsIncludeLabels: true }
        const lsingles = singles2.layout(opts2);
        lsingles.run();
        singles2.shift("y", (singles2.boundingBox({}).y2 + 10) * -1);
        cy.fit();

        // cy.on('select', 'node', function (_: any) {
        //     var node = cy.$('node:selected');
        //     if (node.nonempty()) {
        //         goToNode(node.data('id'));
        //     }
        // });
    }


    /// Double click
    let tappedBefore : EventTarget;
    let tappedTimeout : NodeJS.Timeout;

    cy.on('tap', function (event : EventObject) {
        const tappedNow = event.target;
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
    cy.on('doubleTap', 'node', function (event) {
        goToNode(event.target.data('location'));
    });

    cy.on('mouseover', 'node', function (e) {
        const sel = e.target;
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
        const sel = e.target;
        cy.elements()
            .removeClass('semitransp');
        sel.removeClass('highlight')
            .outgoers()
            .union(sel.incomers())
            .removeClass('highlight');
    });

    cy.on("render", function () {
        layer.resetTransform(ctx);
        layer.clear(ctx);


        layer.setTransform(ctx);

        drawExtra(cy.nodes(), ctx, cy.zoom())
    });
    function debounce<T, R>(func : ((input: T) => (R)), wait : number, immediate : boolean) {
        // 'private' variable for instance
        // The returned function will be able to reference this due to closure.
        // Each call to the returned function will share this common timer.
        let timeout : NodeJS.Timeout;

        // Calling debounce returns a new anonymous function
        return function (...args: T[]) {
            // Should the function be called now? If immediate is true
            //   and not already in a timeout then the answer is: Yes
            const callNow = immediate && !timeout;

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
                    func.apply(this, args);
                }
            }, wait);

            // Immediate mode and no wait timer? Execute the function..
            if (callNow) func.apply(this, args);
        }
    }
    function resizeme() {
        $("#cy").width(10);
        cy.resize();
        cy.center();
    }
    cy.on("resize", function () {
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

export function goToNode(location : techNode["location"]) {
    // var node = _data.filter(x => x.id === id)[0];
    const uri = location.filename
    const line = location.line
    const column = location.column
    vscode.postMessage({"command": "goToFile", "uri": uri, "line": line, "column": column})
}

export function exportImage(pixelRatio: number) {

    const png = _cy.png({ full: true, output: 'base64uri', scale: pixelRatio });
    const boundingBox = _cy.elements().boundingBox({})
    const canvas = new OffscreenCanvas(Math.ceil(boundingBox.x2 - boundingBox.x1) * pixelRatio, Math.ceil(boundingBox.y2 - boundingBox.y1) * pixelRatio)

    const ctx = canvas.getContext("2d") as unknown as CanvasRenderingContext2D;

    ctx.scale(pixelRatio, pixelRatio)
    ctx.translate(-1 * boundingBox.x1, -1 * boundingBox.y1)

    drawExtra(_cy.nodes(), ctx, (1/pixelRatio))

    canvas.convertToBlob({ "type":"png"}).then((canvasImage) =>
        {
        const reader = new FileReader()
            reader.onloadend = (function () {
                mergeimages([png, reader.result]).then((final: string) => vscode.postMessage({ "command": "saveImage", "image": final.substring(final.indexOf(',') + 1) }))
            })
            reader.readAsDataURL(canvasImage);
        }
    )
}

export function exportJson() {
    const json = JSON.stringify(_cy.json())
    vscode.postMessage({"command": "saveJson", "json": json});
}


interface Location
{
    filename : string
    line : number
    column : number
}
interface ReferenceDetails
{
    key : string
    isOutgoing : boolean
    label : string
}
interface techNode
{
    name : string
    prereqs : Array<string>
    references: Array<ReferenceDetails>
    id : string
    location: Location
    isPrimary : boolean
    details? : Array<{ key: string, values : Array<string> }>
    entityTypeDisplayName? : string
    abbreviation? : string
    entityType : string
}
interface settings
{
    wheelSensitivity: number
}
interface EdgeInput {
    source : string
    target : string
    label : string
}

export function go(nodesJ: Array<techNode>, settings: settings) {
    //console.log(nodesJ);
    const nodes: Array<techNode> = nodesJ//JSON.parse(nodesJ);
    //console.log(nodes);
    // var nodes2 = nodes.map((a) => a.id);
    const edges = nodes.map((a) => a.references.map((b) => b.isOutgoing ? { source: a.id, target: b.key, label: b.label } : { source: b.key, target: a.id, label: b.label }));
    const edges2 : EdgeInput[] = [].concat(...edges)
    // var nodes3 = edges2.map(a => a[1])
    // let nodes4 : string[] = [].concat(nodes2, nodes3);
    // console.log(nodes2);
    // console.log(edges2);
    //document.getElementById('detailsTarget')!.innerHTML = "Parsing data...";
    //tech(["a", "b", "c", "d"], [["a", "b"],["c","d"]]);
    // var nodesfin = new Set(nodes4)
    const edgesfin = new Set(edges2)
    tech(nodes, [...edgesfin], settings);
}

window.addEventListener('message', event => {

    const message = event.data; // The JSON data our extension sent
    switch (message.command) {
        case 'go':
            go(message.data, message.settings)
            break;
        case 'exportImage':
            exportImage(1);
            break;
        case 'exportJson':
            exportJson();
            break;
        case 'importJson':
            tech([],[],message.settings, JSON.parse(message.json))
            break;
        case 'checkCytoscapeRendered':
            // Check if cytoscape is initialized and has rendered elements
            {
                const rendered = _cy !== undefined &&
                             _cy.elements().length > 0 &&
                             document.getElementById('cy') !== null;
            vscode.postMessage({
                "command": "cytoscapeRenderedResult",
                "rendered": rendered
            });
            break; }
    }
});

vscode.postMessage({ "command": "ready"});

//go("test");