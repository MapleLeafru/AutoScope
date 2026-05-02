import sys
import json
import webbrowser
import datetime
import os

sys.stdout.reconfigure(encoding='utf-8')

# =========================================================
# PATHS
# =========================================================

ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
REPORTS_DIR = os.path.join(ROOT_DIR, "reports")

# =========================================================
# INPUT
# =========================================================

raw = sys.stdin.read()

if not raw:
    raise Exception("Analyzer received empty input")

input_data = json.loads(raw)
data = input_data.get("data", [])

# =========================================================
# CLEANER (CRITICAL FIX)
# =========================================================

def clean_text(value):
    if not isinstance(value, str):
        return value
    return value.encode("utf-8", "ignore").decode("utf-8")

# =========================================================
# FILE NAME
# =========================================================

os.makedirs(REPORTS_DIR, exist_ok=True)

analyzer_name = os.path.splitext(os.path.basename(__file__))[0]
timestamp = datetime.datetime.now().strftime("%Y-%m-%d_%H-%M-%S")

file_name = f"{analyzer_name}_{timestamp}.html"
file_path = os.path.join(REPORTS_DIR, file_name)

# =========================================================
# STATS
# =========================================================

prices = [x.get("price") for x in data if x.get("price")]
avg_price = int(sum(prices) / len(prices)) if prices else 0

# =========================================================
# HTML HEADER
# =========================================================

html = f"""
<!DOCTYPE html>
<html lang="ru">
<head>
<meta charset="UTF-8">
<title>AutoScope Report</title>

<style>
body {{
    font-family: Arial;
    background: #f5f7fa;
    margin: 20px;
}}

h1 {{
    color: #333;
}}

.stats {{
    margin-bottom: 20px;
    padding: 10px;
    background: white;
    border-radius: 8px;
}}

table {{
    width: 100%;
    border-collapse: collapse;
    background: white;
}}

th, td {{
    padding: 8px;
    border-bottom: 1px solid #ddd;
    text-align: left;
}}

th {{
    background: #2c3e50;
    color: white;
}}

tr:hover {{
    background: #f1f1f1;
}}

.price {{
    font-weight: bold;
    color: #27ae60;
}}
</style>

</head>
<body>

<h1>AutoScope Report</h1>

<div class="stats">
    <b>Analyzer:</b> {analyzer_name}<br>
    <b>Total records:</b> {len(data)}<br>
    <b>Average price:</b> {avg_price}
</div>

<table>
<thead>
<tr>
    <th>Brand</th>
    <th>Model</th>
    <th>Year</th>
    <th>Price</th>
    <th>Mileage</th>
    <th>Fuel</th>
    <th>Drive</th>
    <th>Region</th>
    <th>Link</th>
</tr>
</thead>

<tbody>
"""

# =========================================================
# ROWS
# =========================================================

for item in data:

    brand = clean_text(item.get("brand", ""))
    model = clean_text(item.get("model", ""))
    fuel = clean_text(item.get("fuel_type", ""))
    drive = clean_text(item.get("drive_type", ""))
    region = clean_text(item.get("sale_region", ""))
    url = clean_text(item.get("url", "#"))

    html += f"""
    <tr>
        <td>{brand}</td>
        <td>{model}</td>
        <td>{item.get("year", "")}</td>
        <td class="price">{item.get("price", "")}</td>
        <td>{item.get("mileage", "")}</td>
        <td>{fuel}</td>
        <td>{drive}</td>
        <td>{region}</td>
        <td><a href="{url}" target="_blank">Open</a></td>
    </tr>
    """

# =========================================================
# FOOTER
# =========================================================

html += """
</tbody>
</table>

</body>
</html>
"""

# =========================================================
# SAVE (SAFE)
# =========================================================

with open(file_path, "w", encoding="utf-8", errors="ignore") as f:
    f.write(html)

webbrowser.open(f"file://{file_path}")

print(json.dumps({
    "status": "ok",
    "file": file_path
}, ensure_ascii=False))