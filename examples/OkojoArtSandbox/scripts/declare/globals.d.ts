/**
 * Requests a sketch canvas size in pixels.
 * @param width Requested canvas width.
 * @param height Requested canvas height.
 */
declare function createCanvas(width?: number, height?: number): void;

/**
 * Clears the canvas with a parsed color string.
 */
declare function background(color: string): void;

/**
 * Clears the canvas with a grayscale value.
 */
declare function background(gray: number): void;

/**
 * Clears the canvas with a grayscale value and alpha channel.
 */
declare function background(gray: number, alpha: number): void;

/**
 * Clears the canvas with RGB color channels.
 */
declare function background(r: number, g: number, b: number): void;

/**
 * Clears the canvas with RGBA color channels.
 */
declare function background(r: number, g: number, b: number, a: number): void;

/**
 * Clears the canvas to transparent black.
 */
declare function clear(): void;

/**
 * Sets the fill color from a parsed color string.
 */
declare function fill(color: string): void;

/**
 * Sets the fill color from a grayscale value.
 */
declare function fill(gray: number): void;

/**
 * Sets the fill color from a grayscale value and alpha channel.
 */
declare function fill(gray: number, alpha: number): void;

/**
 * Sets the fill color from RGB color channels.
 */
declare function fill(r: number, g: number, b: number): void;

/**
 * Sets the fill color from RGBA color channels.
 */
declare function fill(r: number, g: number, b: number, a: number): void;

/**
 * Disables fill for subsequent shapes.
 */
declare function noFill(): void;

/**
 * Sets the stroke color from a parsed color string.
 */
declare function stroke(color: string): void;

/**
 * Sets the stroke color from a grayscale value.
 */
declare function stroke(gray: number): void;

/**
 * Sets the stroke color from a grayscale value and alpha channel.
 */
declare function stroke(gray: number, alpha: number): void;

/**
 * Sets the stroke color from RGB color channels.
 */
declare function stroke(r: number, g: number, b: number): void;

/**
 * Sets the stroke color from RGBA color channels.
 */
declare function stroke(r: number, g: number, b: number, a: number): void;

/**
 * Disables stroke rendering for subsequent shapes.
 */
declare function noStroke(): void;

/**
 * Sets the stroke width in pixels.
 * @param width Requested stroke width.
 */
declare function strokeWeight(width: number): void;

/**
 * Draws a circle centered at the given point.
 */
declare function circle(x: number, y: number, diameter: number): void;

/**
 * Draws a rectangle at the given position and size.
 */
declare function rect(x: number, y: number, width: number, height: number): void;

/**
 * Draws a line segment between two points.
 */
declare function line(x1: number, y1: number, x2: number, y2: number): void;

/**
 * Returns a random floating-point value in the requested range.
 */
declare function random(a?: number, b?: number): number;

/**
 * Current sketch canvas width in pixels.
 */
declare const width: number;

/**
 * Current sketch canvas height in pixels.
 */
declare const height: number;

/**
 * Number of completed draw() frames.
 */
declare const frameCount: number;
