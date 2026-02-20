const fs = require('fs');
let code = fs.readFileSync('app/basket.tsx', 'utf8');

// 1. Add GlowText import
if (!code.includes('GlowText')) {
    code = code.replace(
        'import { PageLayout } from "../src/components/page-layout";',
        'import { PageLayout } from "../src/components/page-layout";\nimport { GlowText } from "../src/components/glow-text";'
    );
}

// 2. Add bottom padding to fixedFooter
code = code.replace(
    'className="p-4 bg-black/95"',
    'className="p-4 pb-32 bg-black/95"'
);

// 3. Replace regular Text with GlowText for the price
code = code.replace(
    '<Text className="text-3xl font-black text-white">{discountedTotal} ₴</Text>',
    '<GlowText style={{ fontSize: 32, fontFamily: \'Rajdhani-Bold\' }} color="#FFF" glowColor="#FFF" intensity="high">{discountedTotal} ₴</GlowText>'
);

fs.writeFileSync('app/basket.tsx', code);
