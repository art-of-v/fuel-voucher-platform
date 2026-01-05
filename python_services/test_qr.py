import cv2
import numpy as np
import fitz
import json
import sys

pdf_path = sys.argv[1] if len(sys.argv) > 1 else "test.pdf"

doc = fitz.open(pdf_path)
print(f"Opened PDF: {len(doc)} pages", file=sys.stderr)

for page_num in range(len(doc)):
    page = doc[page_num]
    mat = fitz.Matrix(300/72, 300/72)
    pix = page.get_pixmap(matrix=mat)
    
    img_data = pix.tobytes("png")
    nparr = np.frombuffer(img_data, np.uint8)
    image = cv2.imdecode(nparr, cv2.IMREAD_COLOR)
    
    print(f"Page {page_num+1}: {image.shape}", file=sys.stderr)
    
    detector = cv2.QRCodeDetector()
    retval, decoded_info, points, _ = detector.detectAndDecodeMulti(image)
    
    if retval:
        print(f"Page {page_num+1}: Found {len(decoded_info)} QR codes", file=sys.stderr)
        for i, data in enumerate(decoded_info):
            if data:
                print(f"  QR {i}: {data[:20]}...", file=sys.stderr)
    else:
        print(f"Page {page_num+1}: No QR codes", file=sys.stderr)

doc.close()
print("Done", file=sys.stderr)
