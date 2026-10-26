# yaml documentation sample

Forty fenced blocks, as a documentation site renders them. Generated from short hand-written snippets.

## Example 1

````yaml
name: CI
on:
  push:
    branches: [main]
  pull_request:
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - run: dotnet test
````

## Example 2

````yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: web
  labels:
    app: web
spec:
  replicas: 3
  selector:
    matchLabels:
      app: web
````

## Example 3

````yaml
version: "3.9"
services:
  db:
    image: postgres:16
    environment:
      POSTGRES_PASSWORD: example
    ports:
      - "5432:5432"
````

## Example 4

````yaml
# Site configuration
title: My Docs
baseurl: /docs
markdown: kramdown
plugins:
  - jekyll-feed
  - jekyll-seo-tag
````

## Example 5

````yaml
defaults: &defaults
  adapter: postgres
  pool: 5
development:
  <<: *defaults
  database: app_dev
````

## Example 6

````yaml
description: |
  A multi-line
  literal block.
summary: >
  Folded text that
  becomes one line.
````

## Example 7

````yaml
matrix:
  include:
    - os: windows-latest
      dotnet: "8.0.x"
    - os: macos-latest
      dotnet: "10.0.x"
````

## Example 8

````yaml
key: value
number: 42
float: 3.14
bool: true
nothing: null
date: 2024-05-17
````

## Example 9

````yaml
- name: Install packages
  ansible.builtin.apt:
    name: ["nginx", "git"]
    state: present
  become: true
````

## Example 10

````yaml
openapi: 3.0.0
info:
  title: Sample API
  version: 0.1.9
paths:
  /users:
    get:
      summary: Returns a list of users.
````

## Example 11

````yaml
name: CI
on:
  push:
    branches: [main]
  pull_request:
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - run: dotnet test
````

## Example 12

````yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: web
  labels:
    app: web
spec:
  replicas: 3
  selector:
    matchLabels:
      app: web
````

## Example 13

````yaml
version: "3.9"
services:
  db:
    image: postgres:16
    environment:
      POSTGRES_PASSWORD: example
    ports:
      - "5432:5432"
````

## Example 14

````yaml
# Site configuration
title: My Docs
baseurl: /docs
markdown: kramdown
plugins:
  - jekyll-feed
  - jekyll-seo-tag
````

## Example 15

````yaml
defaults: &defaults
  adapter: postgres
  pool: 5
development:
  <<: *defaults
  database: app_dev
````

## Example 16

````yaml
description: |
  A multi-line
  literal block.
summary: >
  Folded text that
  becomes one line.
````

## Example 17

````yaml
matrix:
  include:
    - os: windows-latest
      dotnet: "8.0.x"
    - os: macos-latest
      dotnet: "10.0.x"
````

## Example 18

````yaml
key: value
number: 42
float: 3.14
bool: true
nothing: null
date: 2024-05-17
````

## Example 19

````yaml
- name: Install packages
  ansible.builtin.apt:
    name: ["nginx", "git"]
    state: present
  become: true
````

## Example 20

````yaml
openapi: 3.0.0
info:
  title: Sample API
  version: 0.1.9
paths:
  /users:
    get:
      summary: Returns a list of users.
````

## Example 21

````yaml
name: CI
on:
  push:
    branches: [main]
  pull_request:
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - run: dotnet test
````

## Example 22

````yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: web
  labels:
    app: web
spec:
  replicas: 3
  selector:
    matchLabels:
      app: web
````

## Example 23

````yaml
version: "3.9"
services:
  db:
    image: postgres:16
    environment:
      POSTGRES_PASSWORD: example
    ports:
      - "5432:5432"
````

## Example 24

````yaml
# Site configuration
title: My Docs
baseurl: /docs
markdown: kramdown
plugins:
  - jekyll-feed
  - jekyll-seo-tag
````

## Example 25

````yaml
defaults: &defaults
  adapter: postgres
  pool: 5
development:
  <<: *defaults
  database: app_dev
````

## Example 26

````yaml
description: |
  A multi-line
  literal block.
summary: >
  Folded text that
  becomes one line.
````

## Example 27

````yaml
matrix:
  include:
    - os: windows-latest
      dotnet: "8.0.x"
    - os: macos-latest
      dotnet: "10.0.x"
````

## Example 28

````yaml
key: value
number: 42
float: 3.14
bool: true
nothing: null
date: 2024-05-17
````

## Example 29

````yaml
- name: Install packages
  ansible.builtin.apt:
    name: ["nginx", "git"]
    state: present
  become: true
````

## Example 30

````yaml
openapi: 3.0.0
info:
  title: Sample API
  version: 0.1.9
paths:
  /users:
    get:
      summary: Returns a list of users.
````

## Example 31

````yaml
name: CI
on:
  push:
    branches: [main]
  pull_request:
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - run: dotnet test
````

## Example 32

````yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: web
  labels:
    app: web
spec:
  replicas: 3
  selector:
    matchLabels:
      app: web
````

## Example 33

````yaml
version: "3.9"
services:
  db:
    image: postgres:16
    environment:
      POSTGRES_PASSWORD: example
    ports:
      - "5432:5432"
````

## Example 34

````yaml
# Site configuration
title: My Docs
baseurl: /docs
markdown: kramdown
plugins:
  - jekyll-feed
  - jekyll-seo-tag
````

## Example 35

````yaml
defaults: &defaults
  adapter: postgres
  pool: 5
development:
  <<: *defaults
  database: app_dev
````

## Example 36

````yaml
description: |
  A multi-line
  literal block.
summary: >
  Folded text that
  becomes one line.
````

## Example 37

````yaml
matrix:
  include:
    - os: windows-latest
      dotnet: "8.0.x"
    - os: macos-latest
      dotnet: "10.0.x"
````

## Example 38

````yaml
key: value
number: 42
float: 3.14
bool: true
nothing: null
date: 2024-05-17
````

## Example 39

````yaml
- name: Install packages
  ansible.builtin.apt:
    name: ["nginx", "git"]
    state: present
  become: true
````

## Example 40

````yaml
openapi: 3.0.0
info:
  title: Sample API
  version: 0.1.9
paths:
  /users:
    get:
      summary: Returns a list of users.
````
