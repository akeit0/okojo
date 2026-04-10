function setup() {
    createCanvas(960, 720);
    strokeWeight(1.5);
}

function draw() {
    background("orange");

    const cx = width * 0.5;
    const cy = height * 0.5;
    const petals = 180;
    const t = frameCount * 0.013;
    for (let i = 0; i < petals; i++) {
        const p = i / petals;
        const angle = p * Math.PI * 10 + t;
        const radius = 30 + p * Math.min(width, height) * 0.42;
        const wobble = Math.sin(t * 2 + p * 30) * 24;
        const x = cx + Math.cos(angle) * (radius + wobble);
        const y = cy + Math.sin(angle) * (radius - wobble * 0.35);
        const size = 6 + (1 - p) * 24 + Math.sin(t + p * 18) * 3;

        fill(240 - p * 120, 110 + p * 110, 170 + Math.sin(t + p * 8) * 60);
        stroke("red");
        circle(x, y, size);
        stroke(80 + p * 90, 42);
        line(cx, cy, x, y);
    }

    noStroke();
    for (let i = 0; i < 18; i++) {
        const angle = t * 0.4 + i * (Math.PI * 2 / 18);
        const ring = 120 + i * 18 + Math.sin(t * 3 + i) * 10;
        const x = cx + Math.cos(angle) * ring;
        const y = cy + Math.sin(angle) * ring;
        fill(80, 70);
        circle(x, y, 8 + Math.sin(t * 4 + i) * 4);
    }
}
