# Build from the frontend folder:  docker build -t kira-web .
FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json* ./
RUN if [ -f package-lock.json ]; then npm ci; else npm install; fi
COPY . .
RUN npx ng build --configuration production

FROM nginx:1.29-alpine
COPY nginx.conf /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/kira-web/browser /usr/share/nginx/html
# The API container's address; nginx substitutes it into the template at start-up.
ENV API_UPSTREAM=http://api:8080
EXPOSE 80
