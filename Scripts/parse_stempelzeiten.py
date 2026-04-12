#!/usr/bin/env python3
"""
PDF Stempelzeiten Parser
Aufruf: python3 parse_stempelzeiten.py <pdf_path>
Ausgabe: JSON-Array mit allen geparsten Einträgen
"""
import sys, re, json
try:
    import pdfplumber
except ImportError:
    print(json.dumps({"error": "pdfplumber not installed"}))
    sys.exit(1)

def hhmm_to_decimal(s):
    if not s or not re.match(r'^\d{2}:\d{2}$', s):
        return None
    h, m = s.split(':')
    return round(int(h) + int(m) / 60, 4)

def parse_entry_line(line):
    tokens = line.split()
    if len(tokens) < 8: return None
    if not re.match(r'^\d{4}-\d{2}-\d{2}$', tokens[0]): return None
    if 'Gesamt' in tokens: return None
    try:
        total = float(tokens[-1])
    except ValueError:
        return None
    night_str    = tokens[-2]
    duration_str = tokens[-3]
    if not re.match(r'^\d{2}:\d{2}$', night_str):    return None
    if not re.match(r'^\d{2}:\d{2}$', duration_str): return None
    time_in_date, time_in_time   = tokens[1], tokens[2]
    time_out_date, time_out_time = tokens[3], tokens[4]
    if not re.match(r'^\d{4}-\d{2}-\d{2}$', time_in_date):  return None
    if not re.match(r'^\d{2}:\d{2}:\d{2}$', time_in_time):  return None
    if not re.match(r'^\d{4}-\d{2}-\d{2}$', time_out_date): return None
    if not re.match(r'^\d{2}:\d{2}:\d{2}$', time_out_time): return None
    comment_tokens = tokens[5:-3]
    return {
        'entry_date':     tokens[0],
        'time_in':        f"{time_in_date}T{time_in_time}",
        'time_out':       f"{time_out_date}T{time_out_time}",
        'comment':        ' '.join(comment_tokens) if comment_tokens else None,
        'duration_hours': hhmm_to_decimal(duration_str),
        'night_hours':    hhmm_to_decimal(night_str),
        'total_hours':    total,
    }

if len(sys.argv) < 2:
    print(json.dumps({"error": "No PDF path given"}))
    sys.exit(1)

records = []
current_emp = None
try:
    with pdfplumber.open(sys.argv[1]) as pdf:
        for page in pdf.pages:
            text = page.extract_text()
            if not text: continue
            for line in text.split('\n'):
                line = line.strip()
                m = re.match(r'^(.+?)\s+#(\d+)\s*$', line)
                if m:
                    current_emp = m.group(2)
                    continue
                if current_emp is None: continue
                entry = parse_entry_line(line)
                if entry:
                    entry['employee_number'] = current_emp
                    records.append(entry)
except Exception as e:
    print(json.dumps({"error": str(e)}))
    sys.exit(1)

print(json.dumps(records))
