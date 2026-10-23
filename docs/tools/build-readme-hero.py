"""Build a self-contained SVG cover from the published app screenshots."""
import base64
from pathlib import Path

images = Path(__file__).resolve().parents[1] / "images"


def screenshot(name):
    return "data:image/png;base64," + base64.b64encode((images / name).read_bytes()).decode("ascii")


svg = f'''<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="1280" height="760" viewBox="0 0 1280 760" role="img" aria-labelledby="title description">
<title id="title">MonitorShade</title>
<desc id="description">Windows display controls. Actual morning and evening app screenshots, with linked brightness, OLED protection, and window layout.</desc>
<defs>
  <linearGradient id="bg" x2="1" y2="1"><stop stop-color="#17221e"/><stop offset=".6" stop-color="#121817"/><stop offset="1" stop-color="#271f29"/></linearGradient>
  <radialGradient id="sage"><stop stop-color="#c6e4a2" stop-opacity=".3"/><stop offset="1" stop-color="#78b295" stop-opacity="0"/></radialGradient>
  <radialGradient id="peach"><stop stop-color="#ee9765" stop-opacity=".36"/><stop offset="1" stop-color="#c36e9e" stop-opacity="0"/></radialGradient>
  <linearGradient id="rim"><stop stop-color="#e6f5d2" stop-opacity=".8"/><stop offset=".5" stop-color="#e8c5a5" stop-opacity=".12"/><stop offset="1" stop-color="#edb4d0" stop-opacity=".65"/></linearGradient>
  <pattern id="dots" width="22" height="22" patternUnits="userSpaceOnUse"><circle cx="1" cy="1" r=".75" fill="#dcf1d7" opacity=".12"/></pattern>
  <filter id="shadow" x="-20%" y="-20%" width="145%" height="160%"><feDropShadow dx="0" dy="26" stdDeviation="28" flood-color="#000" flood-opacity=".6"/></filter>
  <clipPath id="canvas"><rect width="1280" height="760" rx="28"/></clipPath>
  <clipPath id="screen"><rect width="1000" height="746" rx="17"/></clipPath>
</defs>
<g clip-path="url(#canvas)">
  <rect width="1280" height="760" fill="url(#bg)"/>
  <ellipse cx="360" cy="90" rx="570" ry="480" fill="url(#sage)"/>
  <ellipse cx="1080" cy="530" rx="520" ry="480" fill="url(#peach)"/>
  <rect width="1280" height="760" fill="url(#dots)"/>
  <ellipse cx="873" cy="435" rx="374" ry="264" transform="rotate(-20 873 435)" fill="none" stroke="url(#rim)" stroke-opacity=".27"/>
  <g font-family="Segoe UI,Arial,sans-serif">
    <circle cx="70" cy="57" r="4" fill="#c6e4a2"/>
    <text x="85" y="62" font-size="12" letter-spacing="2.5" fill="#c0cbbb">WINDOWS</text>
    <rect x="1108" y="37" width="104" height="35" rx="17.5" fill="#ffffff" fill-opacity=".04" stroke="#ffffff" stroke-opacity=".16"/>
    <text x="1160" y="60" text-anchor="middle" font-size="13" fill="#d8e2d3">v1.18.2</text>
    <text x="62" y="150" font-size="67" font-weight="600" letter-spacing="-3" fill="#f1f4e9">MonitorShade</text>
    <g font-size="24" fill="#e4e9df">
      <text x="112" y="297">Brightness</text>
      <text x="112" y="374">OLED Protection</text>
      <text x="112" y="451">Window Layout</text>
    </g>
    <g font-size="12" fill="#9cae97" letter-spacing="1">
      <text x="66" y="292">01</text><text x="66" y="369">02</text><text x="66" y="446">03</text>
    </g>
    <path d="M66 323h300M66 400h300M66 477h300" stroke="#d8e6cd" stroke-opacity=".14"/>
    <text x="66" y="667" font-size="12" letter-spacing="2" fill="#bdcbb6">DDC/CI</text>
    <text x="66" y="696" font-size="12" fill="#859580">x64  /  KO + EN</text>
  </g>
  <g transform="translate(650 120) rotate(8) scale(.49)" filter="url(#shadow)">
    <rect x="-3" y="-3" width="1006" height="752" rx="20" fill="#d3d5cb"/>
    <image xlink:href="{screenshot('appearance-morning.png')}" width="1000" height="746" clip-path="url(#screen)"/>
  </g>
  <g transform="translate(440 216) rotate(-7) scale(.68)" filter="url(#shadow)">
    <rect x="-3" y="-3" width="1006" height="752" rx="20" fill="#1b2220" stroke="url(#rim)" stroke-width="2"/>
    <image xlink:href="{screenshot('appearance-evening.png')}" width="1000" height="746" clip-path="url(#screen)"/>
  </g>
  <rect x=".5" y=".5" width="1279" height="759" rx="28" fill="none" stroke="#e3edda" stroke-opacity=".13"/>
</g>
</svg>'''
(images / "hero.svg").write_text(svg, encoding="utf-8")
print(f"Built {images / 'hero.svg'}")
