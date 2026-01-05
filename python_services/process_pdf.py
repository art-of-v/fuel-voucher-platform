import cv2
import numpy as np
import easyocr
import re
import json
import os
import fitz
from pathlib import Path
from typing import List, Dict, Any, Tuple, Optional

DEBUG_DIR = "ocr_debug"
os.makedirs(DEBUG_DIR, exist_ok=True)

reader = easyocr.Reader(['ru', 'en'], gpu=False)

def normalize_fuel_name(text: str) -> Optional[str]:
    upper = text.upper().replace('0', 'O').replace('€', 'Є')
    
    if re.search(r'P\s*U\s*L\s*L\s*S', upper) or "PULL" in upper:
        if "95" in upper: return "PULLS 95"
        if "100" in upper: return "PULLS 100"
        return "ДП PULLS"
    
    if "MUSTANG" in upper or "МУСТАНГ" in upper: return "A-95 Mustang"
    if "A-95" in upper or "А-95" in upper or "A95" in upper or "А95" in upper: return "A-95"
    if "A-92" in upper or "А-92" in upper or "A92" in upper or "А92" in upper: return "A-92"
    if "A-100" in upper or "А-100" in upper or "A100" in upper or "А100" in upper: return "A-100"
    if any(x in upper for x in ["ДП", "DIESEL", "EURO", "ЄВРО", "ВРО"]): return "ДП ЄВРО"
    if "ГАЗ" in upper or "GAS" in upper or "LPG" in upper: return "ГАЗ"
    
    return None

def parse_date(text: str) -> Optional[str]:
    match = re.search(r'\b(\d{2})[\.,/](\d{2})[\.,/](\d{2,4})\b', text)
    if match:
        year = match.group(3)
        if len(year) == 2:
            year = "20" + year
        elif len(year) == 3:
            year = "202" + year[-1]
        return f"{year}-{match.group(2)}-{match.group(1)}"
    return None

def parse_amount(text: str) -> Optional[int]:
    text = text.replace('O', '0').replace('o', '0')
    
    match = re.search(r'\b(\d+(?:[\.,]\d+)?)\s*(?:л|l|L|літр|liters)\b', text, re.IGNORECASE)
    if match:
        try:
            val = float(match.group(1).replace(',', '.'))
            if 0 < val < 5000: return int(val)
        except: pass
    
    whitelist = [2, 3, 10, 20, 50, 100]
    clean = text.strip().replace(' ', '').replace(',', '.')
    try:
        val = float(clean)
        if val in whitelist: return int(val)
    except: pass
    
    numbers = re.findall(r'\b(\d+)\b', text)
    for num_str in numbers:
        try:
            val = int(num_str)
            if val >= 2020 and val <= 2035: continue
            if val >= 100: continue
            if 1 <= val <= 100:
                return int(val)
        except: pass
    
    return None

def detect_qr_codes(image: np.ndarray) -> List[Tuple[str, np.ndarray]]:
    detector = cv2.QRCodeDetector()
    
    results = []
    
    retval, decoded_info, points, _ = detector.detectAndDecodeMulti(image)
    if retval:
        for i, data in enumerate(decoded_info):
            if data:
                pts = points[i]
                if len(pts.shape) == 3:
                    pts = pts[0]
                results.append((data, pts.astype(int)))
    
    inv_image = 255 - image
    retval, decoded_info, points, _ = detector.detectAndDecodeMulti(inv_image)
    if retval:
        for i, data in enumerate(decoded_info):
            if data and not any(r[0] == data for r in results):
                pts = points[i]
                if len(pts.shape) == 3:
                    pts = pts[0]
                results.append((data, pts.astype(int)))
    
    return results

def extract_voucher_from_qr(image: np.ndarray, qr_data: str, qr_points: np.ndarray, debug_prefix: str) -> Dict[str, Any]:
    x_min = int(np.min(qr_points[:, 0]))
    y_min = int(np.min(qr_points[:, 1]))
    x_max = int(np.max(qr_points[:, 0]))
    y_max = int(np.max(qr_points[:, 1]))
    
    qr_width = x_max - x_min
    qr_height = y_max - y_min
    
    pad_x = int(qr_width * 2.5)
    pad_y = int(qr_height * 2.5)
    
    crop_x1 = max(0, x_min - pad_x)
    crop_y1 = max(0, y_min - pad_y)
    crop_x2 = min(image.shape[1], x_max + pad_x)
    crop_y2 = min(image.shape[0], y_max + pad_y)
    
    crop = image[crop_y1:crop_y2, crop_x1:crop_x2]
    
    if crop.size == 0:
        return {
            "externalId": qr_data,
            "amount": None,
            "fuelType": None,
            "expirationDate": None,
            "provider": "OKKO",
            "rejectionReason": "Empty crop"
        }
    
    cv2.imwrite(f"{DEBUG_DIR}/{debug_prefix}_crop.png", crop)
    
    scale = 2
    crop_scaled = cv2.resize(crop, None, fx=scale, fy=scale, interpolation=cv2.INTER_CUBIC)
    
    gray = cv2.cvtColor(crop_scaled, cv2.COLOR_BGR2GRAY)
    denoised = cv2.fastNlMeansDenoising(gray, None, 10, 7, 21)
    _, thresh = cv2.threshold(denoised, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
    thresh_bgr = cv2.cvtColor(thresh, cv2.COLOR_GRAY2BGR)
    
    cv2.imwrite(f"{DEBUG_DIR}/{debug_prefix}_preprocessed.png", thresh_bgr)
    
    ocr_result = reader.readtext(crop_scaled, detail=1)
    
    if not ocr_result:
        return {
            "externalId": qr_data,
            "amount": None,
            "fuelType": None,
            "expirationDate": None,
            "provider": "OKKO",
            "rejectionReason": "OCR returned no text"
        }
    
    lines_text = []
    for detection in ocr_result:
        text = detection[1]
        confidence = detection[2]
        if confidence > 0.3:
            lines_text.append(text)
    
    combined_text = " ".join(lines_text)
    
    found_fuel = None
    found_amount = None
    found_date = None
    
    for t in lines_text:
        if not found_fuel:
            found_fuel = normalize_fuel_name(t)
        if not found_amount:
            found_amount = parse_amount(t)
        if not found_date:
            found_date = parse_date(t)
    
    if not found_fuel:
        found_fuel = normalize_fuel_name(combined_text)
    if not found_amount:
        found_amount = parse_amount(combined_text)
    if not found_date:
        found_date = parse_date(combined_text)
    
    rejection_reasons = []
    if not found_fuel:
        rejection_reasons.append("Fuel type not detected")
    if not found_amount:
        rejection_reasons.append("Amount not detected")
    if not found_date:
        rejection_reasons.append("Date not detected")
    
    return {
        "externalId": qr_data,
        "amount": found_amount,
        "fuelType": found_fuel,
        "expirationDate": found_date,
        "provider": "OKKO",
        "rejectionReason": ", ".join(rejection_reasons) if rejection_reasons else None
    }

def process_pdf(pdf_path: str) -> Dict[str, Any]:
    pdf_name = Path(pdf_path).stem
    
    doc = fitz.open(pdf_path)
    
    all_vouchers = []
    errors = []
    
    for page_num in range(len(doc)):
        page = doc[page_num]
        
        mat = fitz.Matrix(300/72, 300/72)
        pix = page.get_pixmap(matrix=mat)
        
        img_data = pix.tobytes("png")
        nparr = np.frombuffer(img_data, np.uint8)
        image = cv2.imdecode(nparr, cv2.IMREAD_COLOR)
        
        qr_codes = detect_qr_codes(image)
        
        if not qr_codes:
            errors.append({
                "page": page_num + 1,
                "error": "No QR codes detected"
            })
            continue
        
        annotated = image.copy()
        
        for qr_idx, (qr_data, qr_points) in enumerate(qr_codes):
            cv2.polylines(annotated, [qr_points], True, (0, 255, 0), 3)
            
            debug_prefix = f"{pdf_name}_p{page_num+1}_qr{qr_idx}"
            
            voucher = extract_voucher_from_qr(image, qr_data, qr_points, debug_prefix)
            all_vouchers.append(voucher)
            
            if voucher.get("rejectionReason"):
                errors.append({
                    "page": page_num + 1,
                    "qr_index": qr_idx,
                    "qr_data": qr_data,
                    "error": voucher["rejectionReason"]
                })
        
        cv2.imwrite(f"{DEBUG_DIR}/{pdf_name}_p{page_num+1}_annotated.png", annotated)
    
    doc.close()
    
    return {
        "vouchers": all_vouchers,
        "errors": errors,
        "total_pages": len(doc),
        "total_qr_codes": len(all_vouchers)
    }

if __name__ == "__main__":
    import sys
    
    if len(sys.argv) < 2:
        print(json.dumps({"error": "Usage: python process_pdf.py <pdf_path>"}))
        sys.exit(1)
    
    pdf_path = sys.argv[1]
    
    if not os.path.exists(pdf_path):
        print(json.dumps({"error": f"File not found: {pdf_path}"}))
        sys.exit(1)
    
    result = process_pdf(pdf_path)
    print(json.dumps(result, ensure_ascii=False, indent=2))
