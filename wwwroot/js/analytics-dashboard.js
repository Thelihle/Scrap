// Analytics Dashboard JavaScript

document.addEventListener('DOMContentLoaded', function () {
    // Initialize DateRangePicker
    initializeDateRangePicker();

    // Load filter options
    loadDepartments();
    loadShifts();

    // Initialize charts
    loadChartData();

    // Load additional data
    loadScrapByReason();
    loadActivityLog();

    // Setup event handlers
    setupEventHandlers();
});

// Chart objects to allow updates
let dailyTrendChart;
let scrapBySectionChart;
let statusDistributionChart;
let topReasonsChart;

function initializeDateRangePicker() {
    $('#daterange').daterangepicker({
        startDate: moment($('#startDate').val()),
        endDate: moment($('#endDate').val()),
        opens: 'left',
        ranges: {
            'Today': [moment(), moment()],
            'Yesterday': [moment().subtract(1, 'days'), moment().subtract(1, 'days')],
            'Last 7 Days': [moment().subtract(6, 'days'), moment()],
            'Last 30 Days': [moment().subtract(29, 'days'), moment()],
            'This Month': [moment().startOf('month'), moment().endOf('month')],
            'Last Month': [moment().subtract(1, 'month').startOf('month'), moment().subtract(1, 'month').endOf('month')]
        }
    }, function (start, end) {
        $('#startDate').val(start.format('YYYY-MM-DD'));
        $('#endDate').val(end.format('YYYY-MM-DD'));
    });
}

function setupEventHandlers() {
    // Filter form submission
    $('#filterForm').on('submit', function (e) {
        e.preventDefault();
        applyFilters();
    });

    // Reset filters
    $('#resetFilters').on('click', function () {
        resetFilters();
    });

    // Department change
    $('#department').on('change', function () {
        loadLinesByDepartment($(this).val());
    });

    // Tab navigation
    $('.tab-btn').on('click', function () {
        const tab = $(this).data('tab');

        // Update active tab button
        $('.tab-btn').removeClass('active');
        $(this).addClass('active');

        // Show selected tab content
        $('.tab-content').removeClass('active');
        $(`#${tab}`).addClass('active');
    });

    // Export buttons
    $('#exportExcel').on('click', function () {
        exportToExcel();
    });

    $('#exportPdf').on('click', function () {
        exportToPdf();
    });
}

function applyFilters() {
    // Show loading indicators
    showLoading();

    const filterData = {
        startDate: $('#startDate').val(),
        endDate: $('#endDate').val(),
        department: $('#department').val(),
        line: $('#line').val(),
        shift: $('#shift').val(),
        __RequestVerificationToken: $('input[name="__RequestVerificationToken"]').val()
    };

    $.ajax({
        url: '/Analytics/FilterData',
        type: 'POST',
        data: JSON.stringify(filterData),
        contentType: 'application/json',
        success: function (response) {
            if (response.success) {
                updateDashboard(response.data);
            } else {
                showError('Error applying filters: ' + response.message);
            }
            hideLoading();
        },
        error: function (xhr, status, error) {
            showError('Error: ' + error);
            hideLoading();
        }
    });
}

function updateDashboard(data) {
    // Update summary stats
    $('#totalEntriesValue').text(data.summary.totalEntries);
    $('#totalQuantityValue').text(data.summary.totalQuantity);
    $('#approvedValue').text(data.summary.approvedCount);
    $('#rejectedValue').text(data.summary.rejectedCount);
    $('#pendingValue').text(data.summary.pendingCount);
    $('#avgWeightValue').text(data.summary.averageWeight.toFixed(2));

    // Update top parts table
    updateTopPartsTable(data.topParts);

    // Update scrap by section table
    updateSectionTable(data.scrapBySection);

    // Update charts
    updateCharts();

    // Reload other data
    loadScrapByReason();
    loadActivityLog();
}

function updateTopPartsTable(topParts) {
    const tableBody = $('#topPartsTable tbody');
    tableBody.empty();

    if (topParts && topParts.length > 0) {
        topParts.forEach(function (part) {
            const row = `
                <tr>
                    <td>${part.partNumber}</td>
                    <td>${part.totalQuantity}</td>
                    <td>${part.occurrenceCount}</td>
                    <td>${formatDateTime(part.lastOccurrence)}</td>
                </tr>
            `;
            tableBody.append(row);
        });
    } else {
        tableBody.append('<tr><td colspan="4" class="no-data">No data available</td></tr>');
    }
}

function updateSectionTable(sections) {
    const tableBody = $('#sectionTable tbody');
    tableBody.empty();

    if (sections && sections.length > 0) {
        sections.forEach(function (section) {
            const row = `
                <tr>
                    <td>${section.scrapSectionName}</td>
                    <td>${section.entryCount}</td>
                    <td>${section.totalQuantity}</td>
                </tr>
            `;
            tableBody.append(row);
        });
    } else {
        tableBody.append('<tr><td colspan="3" class="no-data">No data available</td></tr>');
    }
}

function loadDepartments() {
    $.ajax({
        url: '/Analytics/GetDepartments',
        type: 'GET',
        success: function (response) {
            if (response.success) {
                const departmentSelect = $('#department');
                departmentSelect.find('option:not(:first)').remove();

                response.departments.forEach(function (dept) {
                    departmentSelect.append(`<option value="${dept}">${dept}</option>`);
                });
            }
        }
    });
}

function loadLinesByDepartment(department) {
    const lineSelect = $('#line');
    lineSelect.prop('disabled', !department);
    lineSelect.find('option:not(:first)').remove();

    if (department) {
        $.ajax({
            url: `/Analytics/GetLinesByDepartment?department=${department}`,
            type: 'GET',
            success: function (response) {
                if (response.success) {
                    response.lines.forEach(function (line) {
                        lineSelect.append(`<option value="${line}">${line}</option>`);
                    });
                    lineSelect.prop('disabled', false);
                }
            }
        });
    }
}

function loadShifts() {
    $.ajax({
        url: '/Analytics/GetShifts',
        type: 'GET',
        success: function (response) {
            if (response.success) {
                const shiftSelect = $('#shift');
                shiftSelect.find('option:not(:first)').remove();

                response.shifts.forEach(function (shift) {
                    shiftSelect.append(`<option value="${shift}">${shift}</option>`);
                });
            }
        }
    });
}

function loadChartData() {
    const params = {
        startDate: $('#startDate').val(),
        endDate: $('#endDate').val(),
        department: $('#department').val(),
        line: $('#line').val(),
        shift: $('#shift').val()
    };

    const queryString = Object.keys(params)
        .filter(key => params[key])
        .map(key => `${key}=${encodeURIComponent(params[key])}`)
        .join('&');

    $.ajax({
        url: `/Analytics/GetChartData?${queryString}`,
        type: 'GET',
        success: function (response) {
            if (response.success) {
                initializeCharts(response);
            }
        }
    });
}

function initializeCharts(data) {
    // Daily trend chart
    initializeDailyTrendChart(data.dailyScrap);

    // Scrap by section chart
    initializeScrapBySectionChart(data.scrapBySection);

    // Status distribution chart
    initializeStatusDistributionChart(data.statusDistribution);

    // Load top reasons chart data
    loadTopReasonsChart();
}

function initializeDailyTrendChart(data) {
    const ctx = document.getElementById('dailyTrendChart').getContext('2d');

    // Destroy existing chart if it exists
    if (dailyTrendChart) {
        dailyTrendChart.destroy();
    }

    const labels = data.map(item => item.date);
    const quantities = data.map(item => item.quantity);

    dailyTrendChart = new Chart(ctx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: [{
                label: 'Scrap Quantity',
                data: quantities,
                backgroundColor: 'rgba(0, 255, 252, 0.2)',
                borderColor: 'rgba(0, 255, 252, 1)',
                borderWidth: 2,
                tension: 0.1,
                pointBackgroundColor: 'rgba(0, 255, 252, 1)',
                pointRadius: 3
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            scales: {
                y: {
                    beginAtZero: true,
                    ticks: {
                        color: '#00fffc'
                    },
                    grid: {
                        color: 'rgba(0, 255, 252, 0.1)'
                    }
                },
                x: {
                    ticks: {
                        color: '#00fffc',
                        maxRotation: 45,
                        minRotation: 45
                    },
                    grid: {
                        color: 'rgba(0, 255, 252, 0.1)'
                    }
                }
            },
            plugins: {
                legend: {
                    labels: {
                        color: '#00fffc'
                    }
                }
            }
        }
    });
}

function initializeScrapBySectionChart(data) {
    const ctx = document.getElementById('scrapBySectionChart').getContext('2d');

    // Destroy existing chart if it exists
    if (scrapBySectionChart) {
        scrapBySectionChart.destroy();
    }

    const labels = data.map(item => item.section);
    const quantities = data.map(item => item.quantity);

    // Generate colors for each section
    const backgroundColors = generateColors(labels.length, 0.7);
    const borderColors = generateColors(labels.length, 1);

    scrapBySectionChart = new Chart(ctx, {
        type: 'pie',
        data: {
            labels: labels,
            datasets: [{
                data: quantities,
                backgroundColor: backgroundColors,
                borderColor: borderColors,
                borderWidth: 1
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    position: 'right',
                    labels: {
                        color: '#00fffc'
                    }
                }
            }
        }
    });
}

function initializeStatusDistributionChart(data) {
    const ctx = document.getElementById('statusDistributionChart').getContext('2d');

    // Destroy existing chart if it exists
    if (statusDistributionChart) {
        statusDistributionChart.destroy();
    }

    const labels = data.map(item => item.status);
    const counts = data.map(item => item.count);

    // Define colors for each status
    const backgroundColors = {
        'Approved': 'rgba(0, 255, 0, 0.2)',
        'Rejected': 'rgba(255, 0, 0, 0.2)',
        'Pending': 'rgba(255, 215, 0, 0.2)'
    };

    const borderColors = {
        'Approved': 'rgba(0, 255, 0, 1)',
        'Rejected': 'rgba(255, 0, 0, 1)',
        'Pending': 'rgba(255, 215, 0, 1)'
    };

    const backgroundColor = labels.map(label => backgroundColors[label] || 'rgba(0, 255, 252, 0.2)');
    const borderColor = labels.map(label => borderColors[label] || 'rgba(0, 255, 252, 1)');

    statusDistributionChart = new Chart(ctx, {
        type: 'doughnut',
        data: {
            labels: labels,
            datasets: [{
                data: counts,
                backgroundColor: backgroundColor,
                borderColor: borderColor,
                borderWidth: 1
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    position: 'right',
                    labels: {
                        color: '#00fffc'
                    }
                }
            }
        }
    });
}

function loadTopReasonsChart() {
    const params = {
        startDate: $('#startDate').val(),
        endDate: $('#endDate').val(),
        department: $('#department').val(),
        line: $('#line').val(),
        shift: $('#shift').val()
    };

    const queryString = Object.keys(params)
        .filter(key => params[key])
        .map(key => `${key}=${encodeURIComponent(params[key])}`)
        .join('&');

    $.ajax({
        url: `/Analytics/GetScrapByReason?${queryString}`,
        type: 'GET',
        success: function (response) {
            if (response.success) {
                initializeTopReasonsChart(response.data);

                // Also update the reasons table
                updateReasonsTable(response.data);
            }
        }
    });
}

function initializeTopReasonsChart(data) {
    const ctx = document.getElementById('topReasonsChart').getContext('2d');

    // Destroy existing chart if it exists
    if (topReasonsChart) {
        topReasonsChart.destroy();
    }

    // Limit to top 10 reasons
    const topReasons = data.slice(0, 10);

    const labels = topReasons.map(item => item.reasonName);
    const quantities = topReasons.map(item => item.totalQuantity);

    topReasonsChart = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                label: 'Quantity',
                data: quantities,
                backgroundColor: 'rgba(0, 255, 252, 0.4)',
                borderColor: 'rgba(0, 255, 252, 1)',
                borderWidth: 1
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            scales: {
                y: {
                    beginAtZero: true,
                    ticks: {
                        color: '#00fffc'
                    },
                    grid: {
                        color: 'rgba(0, 255, 252, 0.1)'
                    }
                },
                x: {
                    ticks: {
                        color: '#00fffc',
                        maxRotation: 45,
                        minRotation: 45
                    },
                    grid: {
                        color: 'rgba(0, 255, 252, 0.1)'
                    }
                }
            },
            plugins: {
                legend: {
                    labels: {
                        color: '#00fffc'
                    }
                }
            }
        }
    });
}

function updateReasonsTable(reasons) {
    const tableBody = $('#reasonTable tbody');
    tableBody.empty();

    if (reasons && reasons.length > 0) {
        reasons.forEach(function (reason) {
            const row = `
                <tr>
                    <td>${reason.reasonName}</td>
                    <td>${reason.entryCount}</td>
                    <td>${reason.totalQuantity}</td>
                </tr>
            `;
            tableBody.append(row);
        });
    } else {
        tableBody.append('<tr><td colspan="3" class="no-data">No data available</td></tr>');
    }
}

function loadActivityLog() {
    const params = {
        startDate: $('#startDate').val(),
        endDate: $('#endDate').val()
    };

    const queryString = Object.keys(params)
        .filter(key => params[key])
        .map(key => `${key}=${encodeURIComponent(params[key])}`)
        .join('&');

    $.ajax({
        url: `/Analytics/GetActivityLog?${queryString}`,
        type: 'GET',
        success: function (response) {
            if (response.success) {
                updateActivityTable(response.data);
            }
        }
    });
}

function updateActivityTable(activities) {
    const tableBody = $('#activityTable tbody');
    tableBody.empty();

    if (activities && activities.length > 0) {
        activities.forEach(function (activity) {
            const row = `
                <tr>
                    <td>${activity.username}</td>
                    <td>${activity.activityType}</td>
                    <td>${activity.description}</td>
                    <td>${activity.status}</td>
                    <td>${activity.formattedTimestamp}</td>
                </tr>
            `;
            tableBody.append(row);
        });
    } else {
        tableBody.append('<tr><td colspan="5" class="no-data">No activity data available</td></tr>');
    }
}

function updateCharts() {
    loadChartData();
}

function loadScrapByReason() {
    const params = {
        startDate: $('#startDate').val(),
        endDate: $('#endDate').val(),
        department: $('#department').val(),
        line: $('#line').val(),
        shift: $('#shift').val()
    };

    const queryString = Object.keys(params)
        .filter(key => params[key])
        .map(key => `${key}=${encodeURIComponent(params[key])}`)
        .join('&');

    $.ajax({
        url: `/Analytics/GetScrapByReason?${queryString}`,
        type: 'GET',
        success: function (response) {
            if (response.success) {
                updateReasonsTable(response.data);
            }
        }
    });
}

function exportToExcel() {
    const params = {
        startDate: $('#startDate').val(),
        endDate: $('#endDate').val(),
        department: $('#department').val(),
        line: $('#line').val(),
        shift: $('#shift').val(),
        __RequestVerificationToken: $('input[name="__RequestVerificationToken"]').val()
    };

    // Create a temporary form for the POST request
    const form = document.createElement('form');
    form.method = 'POST';
    form.action = '/Analytics/ExportToExcel';
    form.style.display = 'none';

    // Add form fields
    for (const key in params) {
        if (params[key]) {
            const input = document.createElement('input');
            input.type = 'hidden';
            input.name = key;
            input.value = params[key];
            form.appendChild(input);
        }
    }

    // Submit the form
    document.body.appendChild(form);
    form.submit();
    document.body.removeChild(form);
}

function exportToPdf() {
    const params = {
        startDate: $('#startDate').val(),
        endDate: $('#endDate').val(),
        department: $('#department').val(),
        line: $('#line').val(),
        shift: $('#shift').val(),
        __RequestVerificationToken: $('input[name="__RequestVerificationToken"]').val()
    };

    // Create a temporary form for the POST request
    const form = document.createElement('form');
    form.method = 'POST';
    form.action = '/Analytics/ExportToPdf';
    form.style.display = 'none';

    // Add form fields
    for (const key in params) {
        if (params[key]) {
            const input = document.createElement('input');
            input.type = 'hidden';
            input.name = key;
            input.value = params[key];
            form.appendChild(input);
        }
    }

    // Submit the form
    document.body.appendChild(form);
    form.submit();
    document.body.removeChild(form);
}

function resetFilters() {
    // Reset date range to last 30 days
    const endDate = moment();
    const startDate = moment().subtract(30, 'days');

    $('#daterange').data('daterangepicker').setStartDate(startDate);
    $('#daterange').data('daterangepicker').setEndDate(endDate);

    $('#startDate').val(startDate.format('YYYY-MM-DD'));
    $('#endDate').val(endDate.format('YYYY-MM-DD'));

    // Reset other filters
    $('#department').val('');
    $('#line').val('').prop('disabled', true);
    $('#shift').val('');

    // Apply the reset filters
    applyFilters();
}

function showLoading() {
    // Add loading indicators to key elements
    $('.stat-value').html('<i class="bi bi-arrow-repeat spin"></i>');
    $('.table-container').addClass('loading');
}

function hideLoading() {
    // Remove loading indicators
    $('.table-container').removeClass('loading');
}

function showError(message) {
    alert(message);
}

function formatDateTime(dateTimeStr) {
    if (!dateTimeStr) return '';
    return moment(dateTimeStr).format('YYYY-MM-DD HH:mm');
}

function generateColors(count, alpha) {
    const colors = [];
    const baseColors = [
        [0, 255, 252],      // Cyan
        [0, 191, 255],      // Deep Sky Blue
        [30, 144, 255],     // Dodger Blue
        [65, 105, 225],     // Royal Blue
        [138, 43, 226],     // Blue Violet
        [75, 0, 130],       // Indigo
        [123, 104, 238],    // Medium Slate Blue
        [0, 250, 154],      // Medium Spring Green
        [50, 205, 50],      // Lime Green
        [255, 215, 0],      // Gold
        [255, 165, 0],      // Orange
        [255, 69, 0],       // Red-Orange
        [255, 0, 0],        // Red
        [255, 105, 180],    // Hot Pink
        [255, 20, 147]      // Deep Pink
    ];

    for (let i = 0; i < count; i++) {
        const colorIndex = i % baseColors.length;
        const [r, g, b] = baseColors[colorIndex];
        colors.push(`rgba(${r}, ${g}, ${b}, ${alpha})`);
    }

    return colors;
}