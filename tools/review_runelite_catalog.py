"""Screen public Hub metadata; this is not a plugin code or compatibility audit."""
import collections
from concurrent.futures import ThreadPoolExecutor
import csv
import hashlib
import html
import json
from pathlib import Path
import re
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
URL = 'https://runelite.net/plugin-hub/'
RULES = [
    ('Position / Navigation', r'\b(location|coordinate|navigation|pathfinding|shortest path|world map|minimap|compass|tile marker|tile indicator|destination|route)\b'),
    ('Aufgaben / Quests / Clues', r'\b(quest|quests|clue|clues|puzzle|puzzles|diary|diaries)\b'),
    ('Sammeln / Verarbeitung / XP', r'\b(woodcutting|mining|fishing|cooking|smithing|crafting|runecraft|firemaking|prayer|experience|xp|skilling|resource|resources|ore|ores|tree|trees|fish)\b'),
    ('Inventar / Bank / Items', r'\b(bank|banking|inventory|item|items|equipment|equip|storage|recipe|recipes)\b'),
    ('Zustand / Markierung / Kontrolle', r'\b(idle|notification|notifications|notifier|highlight|highlights|indicator|indicators|tracker|tracking|health|animation|timer|timers|object|objects|npc|npcs)\b'),
    ('Grafik / Leistung / Bedienung', r'\b(fps|performance|memory|gpu|render|renderer|graphics|cursor|camera|detail|texture|textures|hide|hider|interface|overlay)\b'),
    ('Schnittstellen / Daten', r'\b(api|http|websocket|export|exporter|csv|json|telemetry|data)\b'),
]

def core_catalog():
    ref = 'runelite-parent-1.13.4'
    tree_url = f'https://api.github.com/repos/runelite/runelite/git/trees/{ref}?recursive=1'
    tree = json.load(urllib.request.urlopen(tree_url, timeout=60))
    assert not tree.get('truncated'), 'Incomplete core source tree'
    paths = [e['path'] for e in tree['tree'] if re.fullmatch(r'runelite-client/src/main/java/net/runelite/client/plugins/[^/]+/[^/]+Plugin.java',e['path'])]
    def read(path):
        source_url = f'https://raw.githubusercontent.com/runelite/runelite/{ref}/{path}'
        source = urllib.request.urlopen(source_url, timeout=60).read().decode('utf-8')
        descriptor = re.search(r'@PluginDescriptor\((.*?)\)\s', source, re.S)
        if not descriptor:
            assert path.endswith('/config/SearchablePlugin.java') and '@PluginDescriptor(' not in source, f'Unrecognized plugin: {path}'
            return None
        name = re.search(r'\bname\s*=\s*"([^"]+)"', descriptor[1])
        assert name, f'Nonliteral core plugin name: {path}'
        description = re.search(r'\bdescription\s*=\s*"([^"]+)"',descriptor[1])
        terms = name[1] + ' ' + (description[1] if description else '')
        categories = [label for label,rule in RULES if re.search(rule,terms,re.I)]
        return dict(name=name[1],categories='; '.join(categories) or 'Weitere Spezialfunktion / manuell prüfen',url=f'https://github.com/runelite/runelite/blob/{ref}/{path}')
    with ThreadPoolExecutor(max_workers=8) as pool:
        rows = [r for r in pool.map(read,paths) if r]
    return rows, dict(ref=ref,tree_sha=tree['sha'],entries=len(rows),source=tree_url,excluded_helper='config/SearchablePlugin.java (no plugin descriptor)')

def main():
    raw = urllib.request.urlopen(URL, timeout=60).read()
    page = raw.decode('utf-8')
    pattern = r'<h5 class="card-title"><a href="([^"]+)">(.*?)</a></h5>(.*?)(?=<h5 class="card-title">|<script|$)'
    clean = lambda s: html.unescape(re.sub('<[^>]+>', '', s)).strip()
    rows = []
    for url, name, tail in re.findall(pattern, page, re.S):
        paragraphs = re.findall(r'<p class="card-text">(.*?)</p>', tail, re.S)
        description = clean(paragraphs[1]) if len(paragraphs) > 1 else ''
        name = clean(name)
        categories = [label for label, rule in RULES if re.search(rule, name + ' ' + description, re.I)]
        # Category labels are our triage. The original function text remains at source.
        rows.append(dict(name=name, categories='; '.join(categories) or 'Weitere Spezialfunktion / manuell prüfen', url='https://runelite.net'+url))
    advertised = re.search(r'([\d,]+) plugins', clean(page))
    assert advertised and len(rows) == int(advertised[1].replace(',', '')), 'Incomplete catalog extraction'
    assert len({r['url'] for r in rows}) == len(rows), 'Duplicate plugin links'
    assert all(r['name'] for r in rows)
    rows.sort(key=lambda r: r['name'].casefold())
    out = ROOT / 'docs' / 'plugin-review'
    out.mkdir(exist_ok=True)
    with (out/'hub-catalog.csv').open('w', encoding='utf-8-sig', newline='') as f:
        writer = csv.DictWriter(f, fieldnames=['name','categories','url'])
        writer.writeheader(); writer.writerows(rows)
    data = json.dumps(rows, ensure_ascii=False).replace('<', '\\u003c')
    document = '''<!doctype html><html lang="de"><meta charset="utf-8"><title>RuneLite Plugin-Prüfung</title>
<style>body{font:16px system-ui;background:#14202b;color:#edf2f7;margin:32px;max-width:1400px}a{color:#8cd2ff}input,select{font:inherit;padding:10px;background:#243645;color:white;border:1px solid #7592a6;margin:8px 12px 16px 0}input{width:340px}table{border-collapse:collapse;width:100%}td,th{text-align:left;padding:10px;border-bottom:1px solid #435564}th{color:#9ac8df}p{max-width:950px;line-height:1.5}</style>
<h1>RuneLite: vollständiges Hub-Screening</h1><p>Stand 27.10.2026. Alle COUNT Einträge des offiziellen Hub-Katalogs sind erfasst. Kategorien wurden automatisch aus Namen und Funktionsbeschreibungen abgeleitet. Sie sind Prüfkandidaten, keine Installations- oder F2P-Empfehlung. Keine vollständige Quellcode-, Sicherheits- oder Live-Prüfung. Die genaue Funktion steht jeweils auf der verlinkten Plugin-Seite. Eingebaute RuneLite-Plugins sind separat im Bericht behandelt.</p>
<input id="query" aria-label="Plugin suchen" placeholder="Pluginname oder Kategorie suchen"><select id="category" aria-label="Kategorie"><option value="">Alle Kategorien</option></select><span id="count"></span>
<table><thead><tr><th>Plugin / Funktionsseite</th><th>Prüfbereich (heuristisch)</th></tr></thead><tbody id="rows"></tbody></table>
<script>const data=DATA;const q=document.querySelector('#query'), c=document.querySelector('#category');
for(const label of [...new Set(data.flatMap(r=>r.categories.split('; ')))].sort()){const o=document.createElement('option');o.value=o.textContent=label;c.append(o)}
function render(){const filtered=data.filter(r=>(r.name+' '+r.categories).toLowerCase().includes(q.value.toLowerCase())&&(!c.value||r.categories.split('; ').includes(c.value)));document.querySelector('#count').textContent=filtered.length+' / '+data.length;const body=document.querySelector('#rows');body.replaceChildren();for(const r of filtered){const tr=document.createElement('tr'),a=document.createElement('a'),td=document.createElement('td'),cat=document.createElement('td');a.href=r.url;a.textContent=r.name;td.append(a);cat.textContent=r.categories;tr.append(td,cat);body.append(tr)}}q.oninput=c.onchange=render;render();</script></html>'''
    (out/'hub-catalog.html').write_text(document.replace('COUNT',str(len(rows))).replace('DATA',data), encoding='utf-8')
    counts = collections.Counter(cat for r in rows for cat in r['categories'].split('; '))
    metadata = dict(source=URL, retrieved='2026-10-27', html_sha256=hashlib.sha256(raw).hexdigest(), entries=len(rows), category_counts=dict(counts), method='Keyword screening of all names and descriptions; overlapping categories; no code/security/compatibility audit')
    (out/'catalog-provenance.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(metadata, ensure_ascii=False, indent=2))
    core, core_meta = core_catalog()
    core.sort(key=lambda r:r['name'].casefold())
    with (out/'core-catalog.csv').open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=['name','categories','url']);writer.writeheader();writer.writerows(core)
    core_doc = document.replace('vollständiges Hub-Screening','eingebaute Plugins').replace('Alle COUNT Einträge des offiziellen Hub-Katalogs','Alle COUNT Plugin-Deskriptoren aus RuneLite 1.13.4').replace('Eingebaute RuneLite-Plugins sind separat im Bericht behandelt.','Quelle: offizieller RuneLite-Quellcode, Tag runelite-parent-1.13.4.')
    (out/'core-catalog.html').write_text(core_doc.replace('COUNT',str(len(core))).replace('DATA',json.dumps(core,ensure_ascii=False).replace('<','\\u003c')),encoding='utf-8')
    metadata['core']=core_meta
    (out/'catalog-provenance.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(core_meta,indent=2))

if __name__ == '__main__': main()
