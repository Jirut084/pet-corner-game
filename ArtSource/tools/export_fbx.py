"""ส่งออกวัตถุที่เลือกเป็น FBX หนึ่งไฟล์สำหรับ Unity (Blender 3.5)

วิธีใช้:
1. แก้ EXPORT_DIRECTORY ด้านล่างให้เป็นโฟลเดอร์ปลายทางของโปรเจกต์
2. ตั้ง Scene Units เป็น Metric, Unit Scale = 1 เพื่อใช้ 1 Blender unit = 1 เมตร
3. เตรียมโมเดลให้หน้าหันไป -Y, ด้านบน +Z และ origin อยู่กลางฐานที่โลก (0, 0, 0)
   กล่องทดสอบ: Dimensions = 1, 1, 1 เมตร, ฐาน Z = 0, origin กลางฐาน
4. อยู่ใน Object Mode เลือก mesh/armature ที่ต้องการ และตั้ง active object เป็นชื่อไฟล์
5. Scripting > Open ไฟล์นี้ > Run Script (Alt+P เมื่อโฟกัส Text Editor)

ได้ <ชื่อ active object>.fbx รวมวัตถุที่เลือกไว้ในไฟล์เดียว
เมื่อรันซ้ำจะเขียนทับ FBX ชื่อเดิม ไม่บันทึก .blend และไม่แก้ transform ของต้นฉบับ
Unity ใช้ค่าตอน import เริ่มต้นและ Transform Scale = (1, 1, 1)
สคริปต์รักษาตำแหน่งวัตถุ จึงควรตั้ง origin กลางฐานที่โลกก่อน export
การทดสอบกล่องไม่ยืนยันแอนิเมชันของ rig: bake_space_transform เป็นตัวเลือกทดลองสำหรับ armature
"""

from pathlib import Path

import bpy


# แก้โฟลเดอร์ปลายทางที่นี่ ใช้ absolute path เพื่อไม่ขึ้นกับตำแหน่งไฟล์ .blend
EXPORT_DIRECTORY = Path(
    r"C:\Users\OS\.codex\worktrees\pet-corner-stations\PetCorner\Assets\Art\Stations"
)


def export_selected_fbx():
    """ส่งออกเฉพาะ mesh และ armature ที่เลือก โดยใช้ชื่อ active object เป็นชื่อไฟล์"""
    context = bpy.context
    active = context.view_layer.objects.active
    if context.mode != "OBJECT":
        raise RuntimeError("กรุณาเปลี่ยนเป็น Object Mode ก่อนส่งออก")
    if active is None or active not in context.selected_objects:
        raise RuntimeError("กรุณาเลือกวัตถุและตั้ง active object ก่อนส่งออก")
    if not any(obj.type in {"MESH", "ARMATURE"} for obj in context.selected_objects):
        raise RuntimeError("วัตถุที่เลือกต้องมี Mesh หรือ Armature อย่างน้อยหนึ่งชิ้น")

    # ไม่เปลี่ยนชื่อให้อัตโนมัติ เพื่อให้ชื่อไฟล์ตรงกับ active object เสมอ
    name = active.name
    invalid_characters = '<>:"/\\|?*'
    reserved_names = {"CON", "PRN", "AUX", "NUL"}
    reserved_names.update(f"COM{i}" for i in range(1, 10))
    reserved_names.update(f"LPT{i}" for i in range(1, 10))
    if (
        any(char in invalid_characters or ord(char) < 32 for char in name)
        or name.endswith((" ", "."))
        or name.split(".", 1)[0].upper() in reserved_names
    ):
        raise RuntimeError(f"ชื่อวัตถุใช้เป็นชื่อไฟล์ Windows ไม่ได้: {name!r}")

    directory = Path(bpy.path.abspath(str(EXPORT_DIRECTORY))).expanduser()
    if not directory.is_absolute():
        raise RuntimeError("EXPORT_DIRECTORY ต้องเป็น absolute path หรือ Blender path ที่เริ่มด้วย //")
    directory.mkdir(parents=True, exist_ok=True)
    filepath = directory / f"{name}.fbx"

    result = bpy.ops.export_scene.fbx(
        filepath=str(filepath),
        check_existing=False,
        use_selection=True,
        object_types={"MESH", "ARMATURE"},
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        use_space_transform=True,
        bake_space_transform=True,
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="AUTO",
        embed_textures=False,
        batch_mode="OFF",
    )
    if "FINISHED" not in result:
        raise RuntimeError(f"ส่งออก FBX ไม่สำเร็จ: {result}")
    print(f"ส่งออก FBX สำเร็จ: {filepath}")
    return filepath


if __name__ == "__main__":
    export_selected_fbx()
