const fs = require('fs');
const path = require('path');

const indexPath = path.join(__dirname, 'dist', 'index.html');
if (fs.existsSync(indexPath)) {
    let html = fs.readFileSync(indexPath, 'utf8');
    // Replace defer with type="module"
    html = html.replace(/(<script[^>]+)defer([^>]*>)/g, '$1type="module"$2');
    fs.writeFileSync(indexPath, html);
    console.log('✅ Fixed index.html: replaced defer with type="module"');
} else {
    console.error('❌ index.html not found at', indexPath);
}
