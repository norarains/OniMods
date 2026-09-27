#!/usr/bin/env python3
"""Verify that free-build utility paths retain blueprint placement semantics."""

from __future__ import annotations

from pathlib import Path

from onimcp_verify_parsing import fail, matching_delimiter


def extract_block(text: str, marker: str) -> str:
    marker_index = text.find(marker)
    if marker_index < 0:
        fail(f"missing marker: {marker}")
    open_index = text.find("{", marker_index)
    if open_index < 0:
        fail(f"missing block after marker: {marker}")
    close_index = matching_delimiter(text, open_index, "{", "}")
    return text[open_index : close_index + 1]


def require_order(text: str, markers: tuple[str, ...], label: str) -> None:
    cursor = 0
    for marker in markers:
        position = text.find(marker, cursor)
        if position < 0:
            fail(f"{label}: expected ordered markers {markers}")
        cursor = position + len(marker)


def verify_building_blueprint_safety(
    root: Path, sources: dict[Path, str] | None = None
) -> None:
    build_root = root / "mods" / "OniMcp" / "Tools" / "Impl" / "Build"
    paths = {
        "runtime": build_root / "BuildPlanningRuntimePlacement.cs",
        "placement": build_root / "BuildPlanningUtilityConnect.cs",
        "plan_one": build_root / "BuildPlanningPlanOne.cs",
        "materials": build_root / "BuildPlanningMaterials.cs",
    }
    for path in paths.values():
        if not path.is_file():
            fail(f"required source file not found: {path.relative_to(root)}")

    if sources is None:
        selected = {path: path.read_text(encoding="utf-8") for path in paths.values()}
    else:
        selected = sources

    # Neither ordinary buildings nor utility paths may activate the player's tools.
    for text in selected.values():
        for forbidden in ("TryPlaceUtilityPathNative", "TryPlaceWithBuildTool", "OnLeftClickDown",
                          "OnLeftClickUp", "OnDragTool", "OnMouseMove", "BuildTool.Instance.Activate"):
            if forbidden in text:
                fail("UI-dependent construction path is forbidden: " + forbidden)

    auto_connect = extract_block(
        selected[paths["placement"]],
        "public static McpTool AutoConnectUtility()",
    )
    require_order(
        auto_connect,
        (
            "foreach (var point in path)",
            "TryPlanOne(def.PrefabID, point.x, point.y",
        ),
        "utility auto-connect must use per-cell blueprint planning",
    )
    path_loop = extract_block(auto_connect, "foreach (var point in path)")
    if "TryPlanOne(def.PrefabID, point.x, point.y" not in path_loop:
        fail("utility fallback path loop must call TryPlanOne for each cell")

    plan_one = extract_block(
        selected[paths["plan_one"]],
        "private static Dictionary<string, object> TryPlanOne",
    )
    require_order(
        plan_one,
        (
            "bool completedImmediately = IsAuthorizedVirtualFileInstantBuild(args);",
            "if (completedImmediately)",
            "TryBuildVirtualFileInstantBuild",
            "return InstantCompletionFailureResult",
            "else",
            "def.TryPlace(",
            'return ErrorResult(prefabId, x, y, "Placement failed"',
            "return PowerConnectionReceipt(new Dictionary<string, object>",
            '["blueprintPlaced"] = !completedImmediately,',
            '["buildingCompleted"] = completedImmediately,',
        ),
        "TryPlanOne must distinguish successful blueprints from successful instant completion",
    )

    materials = selected[paths["materials"]]
    select_elements = extract_block(
        materials,
        "private static MaterialSelection SelectElements",
    )
    auto_selection = extract_block(select_elements, "if (auto)")
    require_order(
        auto_selection,
        (
            "var defaults = DefaultBuildElements(def);",
            "if (IsFreeBuildContext())",
            "return ValidatedMaterialSelection(defaults",
            "available.FirstOrDefault()",
        ),
        "free-build auto material selection must prefer ordered building defaults",
    )
    defaults_index = auto_selection.find("var defaults = DefaultBuildElements(def);")
    free_build_index = auto_selection.find("if (IsFreeBuildContext())", defaults_index)
    if "defaults.Count" in auto_selection[defaults_index:free_build_index]:
        fail("free-build defaults must reach validation even when the ordered list is empty")
    if "MaterialSelection.Success(" in select_elements:
        fail("SelectElements success exits must use the unified validation helper")
    if select_elements.count("return ValidatedMaterialSelection(") != 4:
        fail("all four SelectElements success exits must use the unified validation helper")

    validated_success = extract_block(
        materials,
        "private static MaterialSelection ValidatedMaterialSelection",
    )
    require_order(
        validated_success,
        (
            "if (elements == null || elements.Count == 0)",
            "ElementLoader.GetElement(elements[0]) == null",
            "return MaterialSelection.Success(",
        ),
        "material success validation must reject missing or non-element primary materials",
    )
    if validated_success.count("return MaterialSelection.Invalid(") < 2:
        fail("material success validation must reject empty and invalid primary elements")


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    verify_building_blueprint_safety(root)
    print("OK: utility paths use UI-independent per-cell blueprint placement")


if __name__ == "__main__":
    main()
