import * as cy from "cytoscape";

export function registerCytoscapeCanvas(cytoscape: cy.Core) {
    // Early return if cytoscape is not provided
    if (!cytoscape) {
        return;
    }

    const cyCanvas = function (this : cy.Core, args: { pixelRatio: string; zIndex: number }) {
        const container = this.container()!;

        const canvas = document.createElement("canvas");

        container.appendChild(canvas);

        const options: { zIndex: number; pixelRatio: number } = {
            zIndex: 1,
            pixelRatio: window.devicePixelRatio || 1
        };
        if (args.pixelRatio === "auto") {
            options.pixelRatio = window.devicePixelRatio || 1;
        }
        else if (args.pixelRatio) {
            options.pixelRatio = parseFloat(args.pixelRatio);
        }
        if (args.zIndex) {
            options.zIndex = args.zIndex;
        }

        const resize = (() => {
            const width = container.offsetWidth;
            const height = container.offsetHeight;

            const canvasWidth = width * options.pixelRatio;
            const canvasHeight = height * options.pixelRatio;

            canvas.width = canvasWidth;
            canvas.height = canvasHeight;

            canvas.style.width = `${width}px`;
            canvas.style.height = `${height}px`;

            this.trigger("cyCanvas.resize");
        });

        this.on("resize", () => {
            resize();
        });

        const canvasStyles = {
            position: 'absolute',
            top: '0',
            left: '0',
            zIndex: String(options.zIndex)
        } as const satisfies Partial<CSSStyleDeclaration>;

        Object.assign(canvas.style, canvasStyles);
        resize();

        return {
            /**
             * @return {Canvas} The generated canvas
             */
            getCanvas() {
                return canvas;
            },
            /**
             * Helper: Clear the canvas
             * @param {CanvasRenderingContext2D} ctx
             */
            clear: (ctx: CanvasRenderingContext2D) => {
                const width = this.width();
                const height = this.height();
                ctx.save();
                ctx.setTransform(1, 0, 0, 1, 0, 0);
                ctx.clearRect(0, 0, width * options.pixelRatio, height * options.pixelRatio);
                ctx.restore();
            },
            /**
             * Helper: Reset the context transform to an identity matrix
             * @param {CanvasRenderingContext2D} ctx
             */
            resetTransform(ctx: CanvasRenderingContext2D) {
                ctx.setTransform(1, 0, 0, 1, 0, 0);
            },
            /**
             * Helper: Set the context transform to match Cystoscape's zoom & pan
             * @param {CanvasRenderingContext2D} ctx
             */
            setTransform: (ctx: CanvasRenderingContext2D) => {
                const pan = this.pan();
                const zoom = this.zoom();
                ctx.setTransform(1, 0, 0, 1, 0, 0);
                ctx.translate(pan.x * options.pixelRatio, pan.y * options.pixelRatio);
                ctx.scale(zoom * options.pixelRatio, zoom * options.pixelRatio);
            },
        };
    };

    cy.default("core", "cyCanvas", cyCanvas);
}