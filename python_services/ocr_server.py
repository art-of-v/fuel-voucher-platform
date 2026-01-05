from fastapi import FastAPI, UploadFile, File, Form
import easyocr
import numpy as np
import cv2
import re
import json
import os
import time

app = FastAPI()

# Enable model downloading and cache models in local dir
reader = easyocr.Reader(['ru', 'en'], gpu=False, model_storage_directory='.easyocr_models', download_enabled=True)

DEBUG_DIR = "ocr_debug_crops"
os.makedirs(DEBUG_DIR, exist_ok=True)

MIN_CONFIDENCE = 0.35

def normalize_fuel_name(text):
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

def parse_date(text):
    # 1. Look for explicit pattern "26 включно" -> Year 2026
    match_year = re.search(r'\b(20\d{2}|2\d)\s*включно', text.lower())
    found_year = None
    if match_year:
        y = match_year.group(1)
        if len(y) == 2: found_year = "20" + y
        else: found_year = y

    # 2. Look for standard dates
    match = re.search(r'\b(\d{1,2})[\s\.,/-](\d{1,2})[\s\.,/-](\d{2,4})\b', text)
    if match:
        day, month, year = match.groups()
        
        # Handle "202" -> "2026" heuristic (specific to this user's garbled OCR)
        if year == '202':
            year = '2026'
        elif len(year) == 2:
            year = "20" + year
        elif len(year) == 3:
            # Fallback
            year = "202" + year[-1]

        if found_year and year != found_year:
             # If we inferred year from "включно" and date has different year, trust the date IF four digits
             if len(match.group(3)) == 4: pass
             else: year = found_year

        try:
            val_year = int(year)
            # Accept 2022-2035
            if 2022 <= val_year <= 2035:
                return f"{year}-{month.zfill(2)}-{day.zfill(2)}"
        except: pass
    
    return None

def parse_amount(text):
    text = text.replace('O', '0').replace('o', '0')
    
    # Remove dd.mm.yyyy patterns
    text = re.sub(r'\b\d{1,2}[\.,/]\d{1,2}[\.,/]\d{2,4}\b', '', text)
    
    # 0. Remove "26 включно" number to avoid confusing it with liters
    text = re.sub(r'\b26\s*включно', '', text, flags=re.IGNORECASE)

    # 1a. Explicit "10 liters"
    match = re.search(r'\b(\d+(?:[\.,]\d+)?)\s*(?:л|l|L|літр|liters)\b', text, re.IGNORECASE)
    if match:
        try:
            val = float(match.group(1).replace(',', '.'))
            if 0 < val < 5000: return int(val)
        except: pass

    # 1b. Explicit "liters 10" (Reverse)
    match = re.search(r'(?:л|l|L|літр|liters)\s*\.?\s*(\d+(?:[\.,]\d+)?)', text, re.IGNORECASE)
    if match:
        try:
            val = float(match.group(1).replace(',', '.'))
            if 0 < val < 5000: return int(val)
        except: pass
        
    # 2. Stand-alone numbers
    numbers = re.findall(r'\b(\d+)\b', text)
    for num_str in numbers:
        try:
            val = int(num_str)
            if 2020 <= val <= 2035: continue
            # If 26 is found but text contains "включно", ignore it (handled by subst above too)
            
            # Prefer 10, 20, 50 etc over 26
            if val in [10, 20, 30, 40, 50, 100]: return val
            
            if 1 <= val <= 200:
                pass # Candidate, but let's see if we find a "better" one? 
                # For now just return if valid
                return val
        except: pass
    
    return None

def clean_external_id(text):
    numbers = re.findall(r'\d+', text)
    if not numbers: return text
    
    longest_num = max(numbers, key=len)
    if len(longest_num) >= 12:
        return longest_num
        
    return text

@app.post("/scan_v2")
async def scan_page(file: UploadFile = File(...), qrs: str = Form(...)):
    start_time = time.time()
    try:
        qr_list = json.loads(qrs)
        print(f"[OCR_PY] Received scan request with {len(qr_list)} potential QRs")
        
        contents = await file.read()
        nparr = np.frombuffer(contents, np.uint8)
        image = cv2.imdecode(nparr, cv2.IMREAD_COLOR)
        
        if image is None:
            print("[OCR_PY] Error: Failed to decode image")
            return {"error": "Failed to decode image", "vouchers": []}
        
        active_qrs = []
        for i, q in enumerate(qr_list):
            data = q.get('data')
            if not data: continue
            
            clean_id = clean_external_id(data)
            
            active_qrs.append({
                'data': clean_id,
                'x': float(q.get('x', 0)),
                'y': float(q.get('y', 0)),
                'w': float(q.get('w', 100)),
                'h': float(q.get('h', 100)),
                'index': q.get('index', 0)
            })
        
        print(f"[OCR_PY] Active valid QRs to process: {len(active_qrs)}")

        if not active_qrs:
            return {"vouchers": []}

        parsed_vouchers = []
        
        for idx, qr in enumerate(active_qrs):
            print(f"[OCR_PY] Processing QR {idx+1}/{len(active_qrs)}: {qr['data']}")
            idx = qr['index']
            qr_x = int(qr['x'])
            qr_y = int(qr['y'])
            qr_w = int(qr['w'])
            qr_h = int(qr['h'])
            
            # Crop region
            pad_x = int(qr_w * 2.5) 
            pad_y = int(qr_h * 2.5) 
            
            x1 = max(0, qr_x - pad_x)
            y1 = max(0, qr_y - pad_y) 
            x2 = min(image.shape[1], qr_x + qr_w + pad_x)
            y2 = min(image.shape[0], qr_y + qr_h + pad_y) 
            
            crop = image[y1:y2, x1:x2]
            
            if crop.size == 0:
                continue
            
            # Upscale for better OCR
            scale = 2
            crop_scaled = cv2.resize(crop, None, fx=scale, fy=scale, interpolation=cv2.INTER_CUBIC)
            
            result = reader.readtext(crop_scaled, detail=1)
            
            if not result:
                parsed_vouchers.append({
                    "externalId": qr['data'],
                    "amount": None,
                    "fuelType": None,
                    "expirationDate": None,
                    "provider": "OKKO",
                    "rejectionReason": "OCR extract failed"
                })
                continue
            
            valid_lines = []
            for detection in result:
                text = detection[1]
                confidence = detection[2]
                if confidence >= MIN_CONFIDENCE:
                    valid_lines.append(text)
            
            # DEBUG LOG
            with open("ocr_debug_log.txt", "a", encoding="utf-8") as f:
                f.write(f"QR {qr['data']} Lines: {valid_lines}\n")
            
            combined_text = " ".join(valid_lines)
            
            found_fuel = None
            found_amount = None
            found_date = None
            
            # 1. Line-by-line Parse
            for t in valid_lines:
                if not found_amount: found_amount = parse_amount(t)
            
            for t in valid_lines:
                if not found_fuel: found_fuel = normalize_fuel_name(t)
                if not found_date: found_date = parse_date(t)
            
            # 2. Combined Text Parse (Backup)
            if not found_amount: found_amount = parse_amount(combined_text)
            if not found_fuel: found_fuel = normalize_fuel_name(combined_text)
            if not found_date: found_date = parse_date(combined_text)
            
            # Relaxed Logic: Do NOT reject if fields are missing. Return Unknown/None.
            # reason_str = ", ".join(rejection_reasons) + " missing" if rejection_reasons else None
            reason_str = None
            
            parsed_vouchers.append({
                "externalId": qr['data'],
                "amount": found_amount,  # Node.js will handle validation or defaults
                "fuelType": found_fuel,  # Node.js will default to 'Unknown'
                "expirationDate": found_date,  # Node.js will allow null
                "provider": "OKKO",
                "rejectionReason": None # FORCE ACCEPTANCE
            })

        return {"vouchers": parsed_vouchers}

    except Exception as e:
        import traceback
        traceback.print_exc()
        return {"error": str(e), "vouchers": []}

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)
