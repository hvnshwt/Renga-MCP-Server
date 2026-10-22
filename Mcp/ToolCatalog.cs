namespace RengaMcpServer.Mcp;

public sealed record ToolSpec(string Name, string Title, string Description, string SchemaJson);

public static class ToolCatalog
{
    public static IReadOnlyList<ToolSpec> Tools { get; } = new[]
    {
        new ToolSpec(
            "renga_ping",
            "Renga connection check",
            "Checks the connection to Renga, returns the product version, and reports the open project, levels, axes, and whether geometry exists.",
            """
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_new_project",
            "Create a new Renga project",
            "Creates a new clean Renga project. Any currently open project is closed and unsaved changes are discarded.",
            """
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_open_project",
            "Open a Renga project",
            "Opens an existing .rnp project. Any currently open project is closed and unsaved changes are discarded.",
            """
            {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string",
                  "description": "Path to the .rnp project file."
                }
              },
              "required": ["path"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_levels",
            "List levels",
            "Lists the levels in the current project with their local IDs and elevations in meters. Use these IDs for walls, columns, and floors.",
            """
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_level",
            "Create a level",
            "Creates a level at the given elevation. Renga's public COM API cannot set a level elevation directly, so the server uses the official IFC import route. This rebuilds the level/axis skeleton and therefore must be used before creating walls, columns, beams, or floors.",
            """
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "Level name, for example 'Level 1'."
                },
                "elevation": {
                  "type": "number",
                  "description": "Level elevation in meters."
                }
              },
              "required": ["name", "elevation"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_axis",
            "Create a coordinate axis",
            "Creates a native Renga Axis (grid line) between two points. Renga's public COM API cannot construct Axis objects directly, so the server uses the official IFC import route. Create all axes and levels before creating geometry.",
            """
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "Axis name, for example 'A' or '1'."
                },
                "x1": {
                  "type": "number",
                  "description": "Start X in meters."
                },
                "y1": {
                  "type": "number",
                  "description": "Start Y in meters."
                },
                "x2": {
                  "type": "number",
                  "description": "End X in meters."
                },
                "y2": {
                  "type": "number",
                  "description": "End Y in meters."
                }
              },
              "required": ["name", "x1", "y1", "x2", "y2"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_wall",
            "Create a wall",
            "Creates a straight wall on a level from a baseline in plan. Coordinates and height are in meters. Use renga_list_levels to obtain levelId.",
            """
            {
              "type": "object",
              "properties": {
                "x1": { "type": "number", "description": "Baseline start X in meters." },
                "y1": { "type": "number", "description": "Baseline start Y in meters." },
                "x2": { "type": "number", "description": "Baseline end X in meters." },
                "y2": { "type": "number", "description": "Baseline end Y in meters." },
                "levelId": { "type": "integer", "description": "Local ID of the host level." },
                "height": { "type": "number", "description": "Wall height in meters." }
              },
              "required": ["x1", "y1", "x2", "y2", "levelId", "height"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_column",
            "Create a column",
            "Creates a column at X/Y on a level. Height is in meters. If levelId is omitted, the nearest level to elevation is used; if elevation is omitted, the column starts at the level elevation.",
            """
            {
              "type": "object",
              "properties": {
                "x": { "type": "number", "description": "Column X in meters." },
                "y": { "type": "number", "description": "Column Y in meters." },
                "height": { "type": "number", "description": "Column height in meters." },
                "levelId": { "type": "integer", "description": "Optional local ID of the host level." },
                "elevation": { "type": "number", "description": "Optional absolute column base elevation in meters." }
              },
              "required": ["x", "y", "height"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_beam",
            "Create a beam",
            "Creates a beam between two absolute 3D points. Coordinates are in meters. If levelId is omitted, the nearest level to z1 is used.",
            """
            {
              "type": "object",
              "properties": {
                "x1": { "type": "number", "description": "Start X in meters." },
                "y1": { "type": "number", "description": "Start Y in meters." },
                "z1": { "type": "number", "description": "Start Z in meters." },
                "x2": { "type": "number", "description": "End X in meters." },
                "y2": { "type": "number", "description": "End Y in meters." },
                "z2": { "type": "number", "description": "End Z in meters." },
                "levelId": { "type": "integer", "description": "Optional local ID of the host level." }
              },
              "required": ["x1", "y1", "z1", "x2", "y2", "z2"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_floor",
            "Create a floor slab",
            "Creates a floor (перекрытие) on a level from a closed polygon or a rectangle. Points and thickness are in meters. Provide either points or x1, y1, x2, y2.",
            """
            {
              "type": "object",
              "properties": {
                "levelId": { "type": "integer", "description": "Local ID of the host level." },
                "thickness": { "type": "number", "description": "Floor thickness in meters." },
                "points": {
                  "type": "array",
                  "description": "Closed or open polygon of at least 3 points in meters.",
                  "items": {
                    "type": "object",
                    "properties": {
                      "x": { "type": "number" },
                      "y": { "type": "number" }
                    },
                    "required": ["x", "y"],
                    "additionalProperties": false
                  }
                },
                "x1": { "type": "number", "description": "Rectangle corner 1 X in meters." },
                "y1": { "type": "number", "description": "Rectangle corner 1 Y in meters." },
                "x2": { "type": "number", "description": "Rectangle corner 2 X in meters." },
                "y2": { "type": "number", "description": "Rectangle corner 2 Y in meters." }
              },
              "required": ["levelId", "thickness"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_save_project",
            "Save the Renga project",
            "Saves the current project to the specified .rnp path. The .rnp extension is appended if missing.",
            """
            {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string",
                  "description": "Destination .rnp file path."
                }
              },
              "required": ["path"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_export_ifc",
            "Export the model to IFC",
            "Exports the current Renga model to an IFC file. The .ifc extension is appended if missing.",
            """
            {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string",
                  "description": "Destination .ifc file path."
                }
              },
              "required": ["path"],
              "additionalProperties": false
            }
            """)
    };
}
