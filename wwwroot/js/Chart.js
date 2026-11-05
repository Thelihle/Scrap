// Vanilla JavaScript Chart Implementation (No external dependencies)
function createDonutChart(canvasId, data, labels, colors) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    const centerX = canvas.width / 2;
    const centerY = canvas.height / 2;
    const radius = Math.min(centerX, centerY) - 20;
    const innerRadius = radius * 0.6;

    // Clear canvas
    ctx.clearRect(0, 0, canvas.width, canvas.height);

    // Calculate total and angles
    const total = data.reduce((sum, value) => sum + value, 0);
    let currentAngle = -Math.PI / 2;

    // Draw segments
    data.forEach((value, index) => {
        const sliceAngle = (value / total) * 2 * Math.PI;

        ctx.beginPath();
        ctx.arc(centerX, centerY, radius, currentAngle, currentAngle + sliceAngle);
        ctx.arc(centerX, centerY, innerRadius, currentAngle + sliceAngle, currentAngle, true);
        ctx.closePath();

        ctx.fillStyle = colors[index];
        ctx.fill();
        ctx.strokeStyle = '#001515';
        ctx.lineWidth = 2;
        ctx.stroke();

        currentAngle += sliceAngle;
    });

    // Draw legend
    const legendY = canvas.height - 80;
    let legendX = 20;

    labels.forEach((label, index) => {
        // Draw color square
        ctx.fillStyle = colors[index];
        ctx.fillRect(legendX, legendY, 15, 15);

        // Draw text
        ctx.fillStyle = '#00fffc';
        ctx.font = '12px Rajdhani, sans-serif';
        ctx.fillText(`${label}: ${data[index]}`, legendX + 20, legendY + 12);

        legendX += 120;
    });
}

function createBarChart(canvasId, data, labels, color) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    const padding = 40;
    const chartWidth = canvas.width - (padding * 2);
    const chartHeight = canvas.height - (padding * 2);

    // Clear canvas
    ctx.clearRect(0, 0, canvas.width, canvas.height);

    if (data.length === 0) {
        ctx.fillStyle = '#00fffc';
        ctx.font = '14px Rajdhani, sans-serif';
        ctx.textAlign = 'center';
        ctx.fillText('No data available', canvas.width / 2, canvas.height / 2);
        return;
    }

    const maxValue = Math.max(...data);
    const barWidth = chartWidth / data.length * 0.8;
    const barSpacing = chartWidth / data.length * 0.2;

    // Draw bars
    data.forEach((value, index) => {
        const barHeight = (value / maxValue) * chartHeight;
        const x = padding + (index * (barWidth + barSpacing));
        const y = canvas.height - padding - barHeight;

        // Create gradient
        const gradient = ctx.createLinearGradient(0, y, 0, y + barHeight);
        gradient.addColorStop(0, 'rgba(0, 255, 252, 0.9)');
        gradient.addColorStop(1, 'rgba(0, 105, 202, 0.7)');

        ctx.fillStyle = gradient;
        ctx.fillRect(x, y, barWidth, barHeight);

        // Draw border
        ctx.strokeStyle = '#00fffc';
        ctx.lineWidth = 1;
        ctx.strokeRect(x, y, barWidth, barHeight);

        // Draw label
        ctx.fillStyle = '#00fffc';
        ctx.font = '10px Rajdhani, sans-serif';
        ctx.textAlign = 'center';
        ctx.save();
        ctx.translate(x + barWidth / 2, canvas.height - 10);
        ctx.rotate(-Math.PI / 4);
        ctx.fillText(labels[index] || '', 0, 0);
        ctx.restore();

        // Draw value
        ctx.textAlign = 'center';
        ctx.fillText(value.toString(), x + barWidth / 2, y - 5);
    });

    // Draw axes
    ctx.strokeStyle = 'rgba(0, 255, 252, 0.3)';
    ctx.lineWidth = 1;

    // Y-axis
    ctx.beginPath();
    ctx.moveTo(padding, padding);
    ctx.lineTo(padding, canvas.height - padding);
    ctx.stroke();

    // X-axis
    ctx.beginPath();
    ctx.moveTo(padding, canvas.height - padding);
    ctx.lineTo(canvas.width - padding, canvas.height - padding);
    ctx.stroke();
}

// Updated chart initialization function
function initializeCharts() {
    console.log("Initializing vanilla JS charts...");

    try {
        // Get data from window object
        const approvedCount = window.chartData?.approvedCount || 0;
        const rejectedCount = window.chartData?.rejectedCount || 0;
        const pendingCount = window.chartData?.pendingCount || 0;

        const partLabels = window.chartData?.partLabels || [];
        const partData = window.chartData?.partData || [];

        // Status distribution donut chart
        const statusData = [approvedCount, rejectedCount, pendingCount];
        const statusLabels = ['Approved', 'Rejected', 'Pending'];
        const statusColors = ['rgba(0, 255, 0, 0.7)', 'rgba(255, 50, 109, 0.7)', 'rgba(255, 215, 0, 0.7)'];

        createDonutChart('statusChart', statusData, statusLabels, statusColors);

        // Parts bar chart
        const partsColor = 'rgba(0, 255, 252, 0.8)';
        createBarChart('partsChart', partData, partLabels, partsColor);

        console.log("Charts initialized successfully");

    } catch (error) {
        console.error("Error initializing charts:", error);
    }
}

// Replace the existing initializeCharts function with this one