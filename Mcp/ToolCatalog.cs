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
                "height": { "type": "number", "description": "Wall height in meters." },
                "thickness": { "type": "number", "description": "Optional wall thickness in meters. Uses the Renga default when omitted." }
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
                "elevation": { "type": "number", "description": "Optional absolute column base elevation in meters." },
                "styleId": { "type": "integer", "description": "Optional column style ID. Use renga_list_styles with kind 'column'." }
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
                "levelId": { "type": "integer", "description": "Optional local ID of the host level." },
                "styleId": { "type": "integer", "description": "Optional beam style ID. Use renga_list_styles with kind 'beam'." }
              },
              "required": ["x1", "y1", "z1", "x2", "y2", "z2"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_door",
            "Create a door",
            "Creates a door hosted on a wall. Coordinates, width, and height are in meters. The wall must already exist.",
            """
            {
              "type": "object",
              "properties": {
                "wallId": { "type": "integer", "description": "Local ID of the host wall." },
                "x": { "type": "number", "description": "Door center X on the wall, in meters." },
                "y": { "type": "number", "description": "Door center Y on the wall, in meters." },
                "width": { "type": "number", "description": "Door width in meters." },
                "height": { "type": "number", "description": "Door height in meters." },
                "offset": { "type": "number", "description": "Optional vertical offset from the level in meters. Default 0." },
                "styleId": { "type": "integer", "description": "Optional door style ID. Use renga_list_styles with kind 'door'. Default 0 means no style." }
              },
              "required": ["wallId", "x", "y", "width", "height"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_window",
            "Create a window",
            "Creates a window hosted on a wall. Coordinates, width, height, and sill are in meters. The wall must already exist.",
            """
            {
              "type": "object",
              "properties": {
                "wallId": { "type": "integer", "description": "Local ID of the host wall." },
                "x": { "type": "number", "description": "Window center X on the wall, in meters." },
                "y": { "type": "number", "description": "Window center Y on the wall, in meters." },
                "width": { "type": "number", "description": "Window width in meters." },
                "height": { "type": "number", "description": "Window height in meters." },
                "sill": { "type": "number", "description": "Optional sill height from the level in meters. Default 0.9." },
                "styleId": { "type": "integer", "description": "Optional window style ID. Use renga_list_styles with kind 'window'. Default 0 means no style." }
              },
              "required": ["wallId", "x", "y", "width", "height"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_roof",
            "Create a flat roof",
            "Creates a flat roof slab on a level from a closed polygon or a rectangle. Points are in meters, thickness and offset are in meters. Provide either points or x1, y1, x2, y2.",
            """
            {
              "type": "object",
              "properties": {
                "levelId": { "type": "integer", "description": "Local ID of the host level." },
                "thickness": { "type": "number", "description": "Roof thickness in meters." },
                "offset": { "type": "number", "description": "Optional roof base elevation above the level in meters. Default 0." },
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
            "renga_list_objects",
            "List model objects",
            "Lists model objects with local IDs, unique IDs, types, names, and host level IDs. Optionally filters by type name or type GUID.",
            """
            {
              "type": "object",
              "properties": {
                "type": { "type": "string", "description": "Optional type name (wall, door, window, level, ...) or type GUID." },
                "limit": { "type": "integer", "description": "Optional maximum number of objects. Default 500." }
              },
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_get_object",
            "Get object details",
            "Returns object identity, host information, parameters, custom properties, and quantities.",
            """
            {
              "type": "object",
              "properties": {
                "objectId": { "type": "integer", "description": "Local model object ID." },
                "includeProperties": { "type": "boolean", "description": "Include custom properties. Default true." }
              },
              "required": ["objectId"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_parameters",
            "List object parameters",
            "Lists all native parameters of an object with GUIDs, names, types, read-only state, raw values, and human-readable values.",
            """
            {
              "type": "object",
              "properties": {
                "objectId": { "type": "integer", "description": "Local model object ID." }
              },
              "required": ["objectId"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_set_parameter",
            "Set an object parameter",
            "Sets a native parameter by GUID or name. Length and elevation values default to meters, angles to degrees, area to m2, volume to m3, mass to kg. Use unit to override.",
            """
            {
              "type": "object",
              "properties": {
                "objectId": { "type": "integer", "description": "Local model object ID." },
                "parameter": { "type": "string", "description": "Parameter GUID or parameter name." },
                "value": { "type": ["number", "string", "boolean"], "description": "New parameter value." },
                "unit": { "type": "string", "description": "Optional unit: m, mm, cm, deg, rad, m2, mm2, cm2, m3, mm3, cm3, kg, g, t, or raw." }
              },
              "required": ["objectId", "parameter", "value"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_materials",
            "List materials",
            "Lists simple materials and layered materials available in the project.",
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": "string", "description": "Optional filter: all, material, or layered." },
                "limit": { "type": "integer", "description": "Optional maximum number of items per collection. Default 500." }
              },
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_styles",
            "List styles",
            "Lists style collections and their items. Without kind it returns collection names and counts; with kind it returns style IDs and names.",
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": "string", "description": "Optional style kind: beam, column, door, window, plate, element, profile, pipe, duct, material, layered_material, and others." },
                "limit": { "type": "integer", "description": "Optional maximum number of items. Default 500." }
              },
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_assign_material",
            "Assign a material",
            "Assigns a simple material or layered material to an object. Columns and beams typically use MaterialId; walls, floors, and roofs use LayeredMaterialId.",
            """
            {
              "type": "object",
              "properties": {
                "objectId": { "type": "integer", "description": "Local model object ID." },
                "materialId": { "type": "integer", "description": "Material or layered material local ID." },
                "layered": { "type": "boolean", "description": "Optional explicit flag: true for layered material, false for simple material." }
              },
              "required": ["objectId", "materialId"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_material",
            "Create a material",
            "Creates a simple material in the project. Walls, floors, and roofs require layered materials for material assignment.",
            """
            {
              "type": "object",
              "properties": {
                "name": { "type": "string", "description": "Material name." },
                "density": { "type": "number", "description": "Optional mass density in kg/m3." }
              },
              "required": ["name"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_style",
            "Create a style",
            "Creates a native style. Supported kinds: door, window, beam, column, plate. Beam, column, and plate styles may require an existing profileId.",
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": "string", "description": "Style kind: door, window, beam, column, or plate." },
                "name": { "type": "string", "description": "Style name." },
                "materialId": { "type": "integer", "description": "Optional material ID for the style." },
                "profileId": { "type": "integer", "description": "Optional profile ID for beam, column, or plate styles." }
              },
              "required": ["kind", "name"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_style_parameters",
            "List style parameters",
            "Lists parameters of a style entity. Use renga_list_styles to find style IDs.",
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": "string", "description": "Style kind, for example door, window, beam, column, plate, material, or layered_material." },
                "styleId": { "type": "integer", "description": "Local style entity ID." }
              },
              "required": ["kind", "styleId"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_set_style_parameter",
            "Set a style parameter",
            "Sets a parameter of a style entity by GUID or name. The same unit rules as renga_set_parameter apply.",
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": "string", "description": "Style kind." },
                "styleId": { "type": "integer", "description": "Local style entity ID." },
                "parameter": { "type": "string", "description": "Parameter GUID or name." },
                "value": { "type": ["number", "string", "boolean"], "description": "New value." },
                "unit": { "type": "string", "description": "Optional unit: m, mm, cm, deg, rad, m2, m3, kg, raw, etc." }
              },
              "required": ["kind", "styleId", "parameter", "value"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_property_definitions",
            "List custom property definitions",
            "Lists registered custom properties in the project with IDs, names, types, and enumeration values.",
            """
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_register_property",
            "Register a custom property",
            "Registers a new custom property definition for the project. The property must then be assigned to an object type.",
            """
            {
              "type": "object",
              "properties": {
                "name": { "type": "string", "description": "Property name." },
                "type": { "type": "string", "description": "Property type: string, double, length, angle, area, boolean, enumeration, integer, logical, mass, volume." },
                "enumeration": {
                  "type": "array",
                  "description": "Optional enumeration values for type 'enumeration'.",
                  "items": { "type": "string" }
                },
                "propertyId": { "type": "string", "description": "Optional GUID. A new GUID is generated when omitted." }
              },
              "required": ["name", "type"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_assign_property",
            "Assign a custom property to a type",
            "Assigns a registered custom property to an object type, for example wall, door, or window.",
            """
            {
              "type": "object",
              "properties": {
                "propertyId": { "type": "string", "description": "Custom property GUID." },
                "objectType": { "type": "string", "description": "Object type name or type GUID." }
              },
              "required": ["propertyId", "objectType"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_unassign_property",
            "Unassign a custom property from a type",
            "Removes a custom property assignment from an object type.",
            """
            {
              "type": "object",
              "properties": {
                "propertyId": { "type": "string", "description": "Custom property GUID." },
                "objectType": { "type": "string", "description": "Object type name or type GUID." }
              },
              "required": ["propertyId", "objectType"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_unregister_property",
            "Unregister a custom property",
            "Removes a custom property definition from the project.",
            """
            {
              "type": "object",
              "properties": {
                "propertyId": { "type": "string", "description": "Custom property GUID." }
              },
              "required": ["propertyId"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_set_property",
            "Set a custom property value",
            "Sets a custom property value on an object. The property must first be registered and assigned to the object type.",
            """
            {
              "type": "object",
              "properties": {
                "objectId": { "type": "integer", "description": "Local model object ID." },
                "propertyId": { "type": "string", "description": "Custom property GUID." },
                "value": { "type": ["number", "string", "boolean"], "description": "New value." }
              },
              "required": ["objectId", "propertyId", "value"],
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
