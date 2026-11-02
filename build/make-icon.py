from PIL import Image
import os, glob
src = glob.glob(r"E:\C#WorkSpace\PBZKeyForge\assets\icon-raw\*.png")[0]
out_dir = r"E:\C#WorkSpace\PBZKeyForge\assets"
im = Image.open(src).convert("RGB")
im.save(os.path.join(out_dir, "icon.png"), "PNG")
im.save(os.path.join(out_dir, "icon.ico"), format="ICO",
        sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
print("png", im.size, os.path.getsize(os.path.join(out_dir,"icon.png")))
print("ico", os.path.getsize(os.path.join(out_dir,"icon.ico")))
