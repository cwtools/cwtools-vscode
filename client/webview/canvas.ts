// @ts-nocheck

//import cytoscape from 'cytoscape'
export default function () {
    // registers the extension on a cytoscape lib ref
    const register = function (cytoscape : cytoscape.Core) {
        if (!cytoscape) {
            return;
        }

        const cyCanvas = function (args) {
            const cy = this;
            const container = cy.container();

            const canvas = document.createElement("canvas");

            container.appendChild(canvas);

            const defaults = {
                zIndex: 1,
                pixelRatio: "auto",
            };

            const options = Object.assign({}, defaults, args);

            if (options.pixelRatio === "auto") {
                options.pixelRatio = window.devicePixelRatio || 1;
            }

            function resize() {
                const width = container.offsetWidth;
                const height = container.offsetHeight;

                const canvasWidth = width * options.pixelRatio;
                const canvasHeight = height * options.pixelRatio;

                canvas.width = canvasWidth;
                canvas.height = canvasHeight;

                canvas.style.width = `${width}px`;
                canvas.style.height = `${height}px`;

                cy.trigger("cyCanvas.resize");
            }

            cy.on("resize", () => {
                resize();
            });

            const styleMap = {
                'position': 'absolute',
                'top': '0',
                'left': '0',
                'z-index': '${options.zIndex}',
            };

            Object.keys(styleMap).forEach((k) => {
                canvas.style[k] = styleMap[k];
            });

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
                clear(ctx) {
                    const width = cy.width();
                    const height = cy.height();
                    ctx.save();
                    ctx.setTransform(1, 0, 0, 1, 0, 0);
                    ctx.clearRect(0, 0, width * options.pixelRatio, height * options.pixelRatio);
                    ctx.restore();
                },
				/**
				 * Helper: Reset the context transform to an identity matrix
				 * @param {CanvasRenderingContext2D} ctx
				 */
                resetTransform(ctx) {
                    ctx.setTransform(1, 0, 0, 1, 0, 0);
                },
				/**
				 * Helper: Set the context transform to match Cystoscape's zoom & pan
				 * @param {CanvasRenderingContext2D} ctx
				 */
                setTransform(ctx) {
                    const pan = cy.pan();
                    const zoom = cy.zoom();
                    ctx.setTransform(1, 0, 0, 1, 0, 0);
                    ctx.translate(pan.x * options.pixelRatio, pan.y * options.pixelRatio);
                    ctx.scale(zoom * options.pixelRatio, zoom * options.pixelRatio);
                },
            };
        };

        cytoscape("core", "cyCanvas", cyCanvas);
    };

    if (typeof module !== "undefined" && module.exports) {
        // expose as a commonjs module
        module.exports = function (cytoscape) {
            register(cytoscape);
        };
    }

    if (typeof define !== "undefined" && define.amd) {
        // expose as an amd/requirejs module
        define("cytoscape-canvas2", () => register);
    }

    if (typeof cytoscape !== "undefined") {
        // expose to global cytoscape (i.e. window.cytoscape)
        register(cytoscape);
    }
    return register;
}