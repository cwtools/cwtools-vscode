/**
 * Types for the graph data returned by getGraphData command
 */

/**
 * Represents a location in a file
 */
export interface GraphLocation {
    /** File path with forward slashes */
    filename: string;
    /** Line number (1-based) */
    line: number;
    /** Column number (1-based) */
    column: number;
}

/**
 * Represents a reference between graph nodes
 */
export interface GraphReference {
    /** The key/id of the referenced node */
    key: string;
    /** Whether this is an outgoing reference */
    isOutgoing: boolean;
    /** Optional label for the reference */
    label?: string;
}

/**
 * Represents a node in the graph
 */
export interface GraphNode {
    /** Unique identifier for the node */
    id: string;
    /** Display name for the node */
    name?: string;
    /** List of references to other nodes */
    references: GraphReference[];
    /** Location of the node in a file */
    location?: GraphLocation;
    /** Documentation for the node */
    documentation?: string;
    /** Additional details as key-value pairs */
    details?: GraphNodeDetail[];
    /** Whether this is a primary node */
    isPrimary: boolean;
    /** Type of the entity */
    entityType: string;
    /** Display name for the entity type */
    entityTypeDisplayName?: string;
    /** Abbreviation for the node */
    abbreviation?: string;
}

/**
 * Represents a detail entry for a graph node
 */
export interface GraphNodeDetail {
    /** Key for the detail */
    key: string;
    /** Values associated with the key */
    values: string[];
}

/**
 * Complete graph data returned by getGraphData
 */
export type GraphData = GraphNode[];

/**
 * Wrapper function for getGraphData command
 * @param entityType The type of entity to get graph data for
 * @param depth The depth of connections to include
 * @returns Promise with the graph data
 */
export async function getGraphData(entityType: string, depth: number): Promise<GraphData> {
    const commands = await import('vscode').then(vscode => vscode.commands);
    const result = await commands.executeCommand<any[]>("getGraphData", entityType, depth);
    return result as GraphData;
}