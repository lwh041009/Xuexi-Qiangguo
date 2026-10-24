"""Build the project's original, small-size-friendly Windows book icon."""
from pathlib import Path

from PIL import Image, ImageDraw


def main():
    assets = Path(__file__).resolve().parent / "assets"
    assets.mkdir(exist_ok=True)
    scale = 4
    image = Image.new("RGBA", (256 * scale, 256 * scale))
    draw = ImageDraw.Draw(image)

    def points(coords):
        return [(x * scale, y * scale) for x, y in coords]

    draw.rounded_rectangle((8 * scale, 8 * scale, 248 * scale, 248 * scale),
                           radius=42 * scale, fill="#DD232C")
    draw.polygon(points([(45, 72), (79, 65), (122, 82), (122, 190), (81, 173), (45, 181)]), fill="white")
    draw.polygon(points([(134, 82), (177, 65), (211, 72), (211, 181), (175, 173), (134, 190)]), fill="white")
    for offset in (0, 26):
        draw.line(points([(62, 102 + offset), (81, 98 + offset), (105, 108 + offset)]),
                  fill="#DD232C", width=6 * scale, joint="curve")
        draw.line(points([(151, 108 + offset), (175, 98 + offset), (194, 102 + offset)]),
                  fill="#DD232C", width=6 * scale, joint="curve")
    image = image.resize((256, 256), Image.Resampling.LANCZOS)
    image.save(assets / "app.png")
    image.save(assets / "app.ico", sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])


if __name__ == "__main__":
    main()
