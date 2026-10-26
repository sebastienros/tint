# javascript documentation sample

Forty fenced blocks, as a documentation site renders them. Generated from short hand-written snippets.

## Example 1

````javascript
const express = require("express");
const app = express();

app.get("/", (req, res) => {
  res.send("Hello World!");
});

app.listen(3000, () => console.log("Listening on 3000"));
````

## Example 2

````javascript
document.querySelectorAll("button.copy").forEach((button) => {
  button.addEventListener("click", async () => {
    await navigator.clipboard.writeText(button.dataset.text);
    button.textContent = "Copied!";
  });
});
````

## Example 3

````javascript
export default {
  name: "counter",
  data() {
    return { count: 0 };
  },
  methods: {
    increment() { this.count++; },
  },
};
````

## Example 4

````javascript
function* fibonacci() {
  let [a, b] = [0, 1];
  while (true) {
    yield a;
    [a, b] = [b, a + b];
  }
}
````

## Example 5

````javascript
const pattern = /^(\d{4})-(\d{2})-(\d{2})$/;
const [, year, month, day] = "2024-05-17".match(pattern) ?? [];
console.log({ year, month, day });
````

## Example 6

````javascript
fetch("/api/items")
  .then((response) => response.json())
  .then((items) => render(items))
  .catch((error) => console.error(error));
````

## Example 7

````javascript
class Timer {
  #start = performance.now();
  elapsed() {
    return Math.round(performance.now() - this.#start);
  }
}
````

## Example 8

````javascript
module.exports = {
  root: true,
  extends: ["eslint:recommended", "prettier"],
  rules: { "no-unused-vars": "warn", eqeqeq: ["error", "always"] },
};
````

## Example 9

````javascript
const sum = (...numbers) => numbers.reduce((total, n) => total + n, 0);
const average = (values) => (values.length ? sum(...values) / values.length : NaN);
````

## Example 10

````javascript
// Wait for the DOM before running.
window.addEventListener("DOMContentLoaded", () => {
  const el = document.getElementById("app");
  el.innerHTML = `<h1>${title}</h1><p>${new Date().toLocaleDateString()}</p>`;
});
````

## Example 11

````javascript
const express = require("express");
const app = express();

app.get("/", (req, res) => {
  res.send("Hello World!");
});

app.listen(3000, () => console.log("Listening on 3000"));
````

## Example 12

````javascript
document.querySelectorAll("button.copy").forEach((button) => {
  button.addEventListener("click", async () => {
    await navigator.clipboard.writeText(button.dataset.text);
    button.textContent = "Copied!";
  });
});
````

## Example 13

````javascript
export default {
  name: "counter",
  data() {
    return { count: 0 };
  },
  methods: {
    increment() { this.count++; },
  },
};
````

## Example 14

````javascript
function* fibonacci() {
  let [a, b] = [0, 1];
  while (true) {
    yield a;
    [a, b] = [b, a + b];
  }
}
````

## Example 15

````javascript
const pattern = /^(\d{4})-(\d{2})-(\d{2})$/;
const [, year, month, day] = "2024-05-17".match(pattern) ?? [];
console.log({ year, month, day });
````

## Example 16

````javascript
fetch("/api/items")
  .then((response) => response.json())
  .then((items) => render(items))
  .catch((error) => console.error(error));
````

## Example 17

````javascript
class Timer {
  #start = performance.now();
  elapsed() {
    return Math.round(performance.now() - this.#start);
  }
}
````

## Example 18

````javascript
module.exports = {
  root: true,
  extends: ["eslint:recommended", "prettier"],
  rules: { "no-unused-vars": "warn", eqeqeq: ["error", "always"] },
};
````

## Example 19

````javascript
const sum = (...numbers) => numbers.reduce((total, n) => total + n, 0);
const average = (values) => (values.length ? sum(...values) / values.length : NaN);
````

## Example 20

````javascript
// Wait for the DOM before running.
window.addEventListener("DOMContentLoaded", () => {
  const el = document.getElementById("app");
  el.innerHTML = `<h1>${title}</h1><p>${new Date().toLocaleDateString()}</p>`;
});
````

## Example 21

````javascript
const express = require("express");
const app = express();

app.get("/", (req, res) => {
  res.send("Hello World!");
});

app.listen(3000, () => console.log("Listening on 3000"));
````

## Example 22

````javascript
document.querySelectorAll("button.copy").forEach((button) => {
  button.addEventListener("click", async () => {
    await navigator.clipboard.writeText(button.dataset.text);
    button.textContent = "Copied!";
  });
});
````

## Example 23

````javascript
export default {
  name: "counter",
  data() {
    return { count: 0 };
  },
  methods: {
    increment() { this.count++; },
  },
};
````

## Example 24

````javascript
function* fibonacci() {
  let [a, b] = [0, 1];
  while (true) {
    yield a;
    [a, b] = [b, a + b];
  }
}
````

## Example 25

````javascript
const pattern = /^(\d{4})-(\d{2})-(\d{2})$/;
const [, year, month, day] = "2024-05-17".match(pattern) ?? [];
console.log({ year, month, day });
````

## Example 26

````javascript
fetch("/api/items")
  .then((response) => response.json())
  .then((items) => render(items))
  .catch((error) => console.error(error));
````

## Example 27

````javascript
class Timer {
  #start = performance.now();
  elapsed() {
    return Math.round(performance.now() - this.#start);
  }
}
````

## Example 28

````javascript
module.exports = {
  root: true,
  extends: ["eslint:recommended", "prettier"],
  rules: { "no-unused-vars": "warn", eqeqeq: ["error", "always"] },
};
````

## Example 29

````javascript
const sum = (...numbers) => numbers.reduce((total, n) => total + n, 0);
const average = (values) => (values.length ? sum(...values) / values.length : NaN);
````

## Example 30

````javascript
// Wait for the DOM before running.
window.addEventListener("DOMContentLoaded", () => {
  const el = document.getElementById("app");
  el.innerHTML = `<h1>${title}</h1><p>${new Date().toLocaleDateString()}</p>`;
});
````

## Example 31

````javascript
const express = require("express");
const app = express();

app.get("/", (req, res) => {
  res.send("Hello World!");
});

app.listen(3000, () => console.log("Listening on 3000"));
````

## Example 32

````javascript
document.querySelectorAll("button.copy").forEach((button) => {
  button.addEventListener("click", async () => {
    await navigator.clipboard.writeText(button.dataset.text);
    button.textContent = "Copied!";
  });
});
````

## Example 33

````javascript
export default {
  name: "counter",
  data() {
    return { count: 0 };
  },
  methods: {
    increment() { this.count++; },
  },
};
````

## Example 34

````javascript
function* fibonacci() {
  let [a, b] = [0, 1];
  while (true) {
    yield a;
    [a, b] = [b, a + b];
  }
}
````

## Example 35

````javascript
const pattern = /^(\d{4})-(\d{2})-(\d{2})$/;
const [, year, month, day] = "2024-05-17".match(pattern) ?? [];
console.log({ year, month, day });
````

## Example 36

````javascript
fetch("/api/items")
  .then((response) => response.json())
  .then((items) => render(items))
  .catch((error) => console.error(error));
````

## Example 37

````javascript
class Timer {
  #start = performance.now();
  elapsed() {
    return Math.round(performance.now() - this.#start);
  }
}
````

## Example 38

````javascript
module.exports = {
  root: true,
  extends: ["eslint:recommended", "prettier"],
  rules: { "no-unused-vars": "warn", eqeqeq: ["error", "always"] },
};
````

## Example 39

````javascript
const sum = (...numbers) => numbers.reduce((total, n) => total + n, 0);
const average = (values) => (values.length ? sum(...values) / values.length : NaN);
````

## Example 40

````javascript
// Wait for the DOM before running.
window.addEventListener("DOMContentLoaded", () => {
  const el = document.getElementById("app");
  el.innerHTML = `<h1>${title}</h1><p>${new Date().toLocaleDateString()}</p>`;
});
````
