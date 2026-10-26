# typescript documentation sample

Forty fenced blocks, as a documentation site renders them. Generated from short hand-written snippets.

## Example 1

````typescript
interface User {
  id: number;
  name: string;
  email?: string;
}

export function greet(user: User): string {
  return `Hello, ${user.name}!`;
}
````

## Example 2

````typescript
export async function fetchJson<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, init);
  if (!response.ok) {
    throw new Error(`Request failed: ${response.status}`);
  }
  return (await response.json()) as T;
}
````

## Example 3

````typescript
type Shape =
  | { kind: "circle"; radius: number }
  | { kind: "square"; size: number };

const area = (s: Shape): number =>
  s.kind === "circle" ? Math.PI * s.radius ** 2 : s.size * s.size;
````

## Example 4

````typescript
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: { port: 3000 },
});
````

## Example 5

````typescript
class Stack<T> {
  private items: T[] = [];
  push(item: T): void { this.items.push(item); }
  pop(): T | undefined { return this.items.pop(); }
  get size(): number { return this.items.length; }
}
````

## Example 6

````typescript
enum Direction { Up = 1, Down, Left, Right }

function move(direction: Direction, steps = 1): void {
  console.log(`Moving ${Direction[direction]} by ${steps}`);
}
````

## Example 7

````typescript
const routes: Record<string, () => Promise<unknown>> = {
  "/": () => import("./pages/home"),
  "/about": () => import("./pages/about"),
};
````

## Example 8

````typescript
export type DeepPartial<T> = {
  [K in keyof T]?: T[K] extends object ? DeepPartial<T[K]> : T[K];
};
````

## Example 9

````typescript
// Debounce a function call.
export function debounce<F extends (...args: any[]) => void>(fn: F, wait = 100) {
  let timer: ReturnType<typeof setTimeout> | undefined;
  return (...args: Parameters<F>) => {
    clearTimeout(timer);
    timer = setTimeout(() => fn(...args), wait);
  };
}
````

## Example 10

````typescript
const result = items
  .filter((x) => x.active)
  .map(({ id, label }) => ({ id, label: label.trim() }))
  .sort((a, b) => a.label.localeCompare(b.label));
````

## Example 11

````typescript
interface User {
  id: number;
  name: string;
  email?: string;
}

export function greet(user: User): string {
  return `Hello, ${user.name}!`;
}
````

## Example 12

````typescript
export async function fetchJson<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, init);
  if (!response.ok) {
    throw new Error(`Request failed: ${response.status}`);
  }
  return (await response.json()) as T;
}
````

## Example 13

````typescript
type Shape =
  | { kind: "circle"; radius: number }
  | { kind: "square"; size: number };

const area = (s: Shape): number =>
  s.kind === "circle" ? Math.PI * s.radius ** 2 : s.size * s.size;
````

## Example 14

````typescript
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: { port: 3000 },
});
````

## Example 15

````typescript
class Stack<T> {
  private items: T[] = [];
  push(item: T): void { this.items.push(item); }
  pop(): T | undefined { return this.items.pop(); }
  get size(): number { return this.items.length; }
}
````

## Example 16

````typescript
enum Direction { Up = 1, Down, Left, Right }

function move(direction: Direction, steps = 1): void {
  console.log(`Moving ${Direction[direction]} by ${steps}`);
}
````

## Example 17

````typescript
const routes: Record<string, () => Promise<unknown>> = {
  "/": () => import("./pages/home"),
  "/about": () => import("./pages/about"),
};
````

## Example 18

````typescript
export type DeepPartial<T> = {
  [K in keyof T]?: T[K] extends object ? DeepPartial<T[K]> : T[K];
};
````

## Example 19

````typescript
// Debounce a function call.
export function debounce<F extends (...args: any[]) => void>(fn: F, wait = 100) {
  let timer: ReturnType<typeof setTimeout> | undefined;
  return (...args: Parameters<F>) => {
    clearTimeout(timer);
    timer = setTimeout(() => fn(...args), wait);
  };
}
````

## Example 20

````typescript
const result = items
  .filter((x) => x.active)
  .map(({ id, label }) => ({ id, label: label.trim() }))
  .sort((a, b) => a.label.localeCompare(b.label));
````

## Example 21

````typescript
interface User {
  id: number;
  name: string;
  email?: string;
}

export function greet(user: User): string {
  return `Hello, ${user.name}!`;
}
````

## Example 22

````typescript
export async function fetchJson<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, init);
  if (!response.ok) {
    throw new Error(`Request failed: ${response.status}`);
  }
  return (await response.json()) as T;
}
````

## Example 23

````typescript
type Shape =
  | { kind: "circle"; radius: number }
  | { kind: "square"; size: number };

const area = (s: Shape): number =>
  s.kind === "circle" ? Math.PI * s.radius ** 2 : s.size * s.size;
````

## Example 24

````typescript
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: { port: 3000 },
});
````

## Example 25

````typescript
class Stack<T> {
  private items: T[] = [];
  push(item: T): void { this.items.push(item); }
  pop(): T | undefined { return this.items.pop(); }
  get size(): number { return this.items.length; }
}
````

## Example 26

````typescript
enum Direction { Up = 1, Down, Left, Right }

function move(direction: Direction, steps = 1): void {
  console.log(`Moving ${Direction[direction]} by ${steps}`);
}
````

## Example 27

````typescript
const routes: Record<string, () => Promise<unknown>> = {
  "/": () => import("./pages/home"),
  "/about": () => import("./pages/about"),
};
````

## Example 28

````typescript
export type DeepPartial<T> = {
  [K in keyof T]?: T[K] extends object ? DeepPartial<T[K]> : T[K];
};
````

## Example 29

````typescript
// Debounce a function call.
export function debounce<F extends (...args: any[]) => void>(fn: F, wait = 100) {
  let timer: ReturnType<typeof setTimeout> | undefined;
  return (...args: Parameters<F>) => {
    clearTimeout(timer);
    timer = setTimeout(() => fn(...args), wait);
  };
}
````

## Example 30

````typescript
const result = items
  .filter((x) => x.active)
  .map(({ id, label }) => ({ id, label: label.trim() }))
  .sort((a, b) => a.label.localeCompare(b.label));
````

## Example 31

````typescript
interface User {
  id: number;
  name: string;
  email?: string;
}

export function greet(user: User): string {
  return `Hello, ${user.name}!`;
}
````

## Example 32

````typescript
export async function fetchJson<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, init);
  if (!response.ok) {
    throw new Error(`Request failed: ${response.status}`);
  }
  return (await response.json()) as T;
}
````

## Example 33

````typescript
type Shape =
  | { kind: "circle"; radius: number }
  | { kind: "square"; size: number };

const area = (s: Shape): number =>
  s.kind === "circle" ? Math.PI * s.radius ** 2 : s.size * s.size;
````

## Example 34

````typescript
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: { port: 3000 },
});
````

## Example 35

````typescript
class Stack<T> {
  private items: T[] = [];
  push(item: T): void { this.items.push(item); }
  pop(): T | undefined { return this.items.pop(); }
  get size(): number { return this.items.length; }
}
````

## Example 36

````typescript
enum Direction { Up = 1, Down, Left, Right }

function move(direction: Direction, steps = 1): void {
  console.log(`Moving ${Direction[direction]} by ${steps}`);
}
````

## Example 37

````typescript
const routes: Record<string, () => Promise<unknown>> = {
  "/": () => import("./pages/home"),
  "/about": () => import("./pages/about"),
};
````

## Example 38

````typescript
export type DeepPartial<T> = {
  [K in keyof T]?: T[K] extends object ? DeepPartial<T[K]> : T[K];
};
````

## Example 39

````typescript
// Debounce a function call.
export function debounce<F extends (...args: any[]) => void>(fn: F, wait = 100) {
  let timer: ReturnType<typeof setTimeout> | undefined;
  return (...args: Parameters<F>) => {
    clearTimeout(timer);
    timer = setTimeout(() => fn(...args), wait);
  };
}
````

## Example 40

````typescript
const result = items
  .filter((x) => x.active)
  .map(({ id, label }) => ({ id, label: label.trim() }))
  .sort((a, b) => a.label.localeCompare(b.label));
````
