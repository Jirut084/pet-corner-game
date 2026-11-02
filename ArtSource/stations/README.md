# สถานีลงสี T1-3

ไฟล์ต้นฉบับ Blender 3.5 ใช้ Metric, Unit Scale 1.0 และ origin กลางฐานที่ (0, 0, 0)
ด้านหน้าคือ -Y ใน Blender และ +Z เมื่อเข้า Unity

| ไฟล์ | สีหลัก sRGB ใน Unity | ฐาน X × Y ใน Blender (ม.) | สูง (ม.) |
| --- | --- | --- | --- |
| groom_colored.blend | #E673E6 | 0.8532 × 0.8859 | 0.6206 |
| play_colored.blend | #73D973 | 0.8532 × 0.8859 | 0.6954 |
| rest_colored.blend | #FF9940 | 0.8600 × 0.8260 | 0.3000 |

Groom และ Play ใช้รูปทรงจาก Assets/Models/Station_Grooming (1).fbx และ Station_Toys.fbx ตามลำดับ
ปรับขนาด geometry เป็นเมตรก่อน export จึงไม่ต้องตั้ง scale ใน Unity
Rest เป็นเบาะเตี้ยใหม่ ไม่มีราวสูง เพื่อให้จุดวางสัตว์ที่เกมคำนวณจากยอดโมเดลตรงกับผิวเบาะ
สีรองใช้ครีม #F4E8D2 น้ำตาลอ่อน #B99571 และสีอุปกรณ์ #62554F

## ## ส่งออกซ้ำ

1. เปิดไฟล์ .blend ที่ต้องการในโฟลเดอร์นี้ แล้วเลือก mesh สถานี
2. เปิด ArtSource/tools/export_fbx.py ในแท็บ Scripting แล้วกด Run Script
3. FBX จะเข้า Assets/Art/Stations โดยใช้ชื่อ active object

ใช้ exporter เดิมจาก T1-1 โดยไม่แก้ค่าแกน หน่วย หรือการแปลงสี
ไฟล์ต้นฉบับเก็บสี shader เป็น linear ตามระบบของ Blender
ตรวจรหัสสีอีกครั้งหลัง Unity URP import เพราะโปรเจกต์ Built-in แปลงสี material ต่างจาก URP

การตรวจ import ไม่ทดแทนการเล่นจริง ต้องตรวจการวางสัตว์และหน้าต่างซูมในเกมด้วย
