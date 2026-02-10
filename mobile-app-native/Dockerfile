# Use a simple base image since we are just serving static files
FROM nginx:alpine

# Copy the pre-built dist folder from the host context
# This assumes the dist folder is already populated with the mobile-app web build
COPY dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 5000
CMD ["nginx", "-g", "daemon off;"]


