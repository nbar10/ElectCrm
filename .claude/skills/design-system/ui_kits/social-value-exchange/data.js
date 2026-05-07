// Fake fixtures for the Social Value Exchange UI kit
window.electData = {
  user: { name: 'Sarah Mendez', role: 'Procurement Lead', initials: 'SM' },
  org: 'Manchester City Council',

  kpis: [
    { icon: 'bi-graph-up-arrow',   tone: 'gold',  value: '£2.4M',  label: 'Total social value committed' },
    { icon: 'bi-check2-circle',    tone: 'green', value: '87%',     label: 'Delivery against plan' },
    { icon: 'bi-people-fill',      tone: 'blue',  value: '142',     label: 'Active partners' },
    { icon: 'bi-calendar-event',   tone: 'amber', value: '23',      label: 'Reports due this month' },
  ],

  partners: [
    { name: 'Bouygues UK',           sector: 'Construction',     score: 8.4, status: 'on-track',  delivered: '£412k' },
    { name: 'Wates Group',           sector: 'Construction',     score: 7.9, status: 'on-track',  delivered: '£298k' },
    { name: 'Northern Care Alliance',sector: 'Health',           score: 9.1, status: 'on-track',  delivered: '£186k' },
    { name: 'Veolia',                sector: 'Environmental',    score: 6.4, status: 'at-risk',   delivered: '£94k'  },
    { name: 'Mitie',                 sector: 'Facilities',       score: 7.2, status: 'on-track',  delivered: '£221k' },
    { name: 'Capita',                sector: 'Outsourcing',      score: 5.1, status: 'breach',    delivered: '£42k'  },
  ],

  catalogue: [
    { id: 'c1', code: 'NS1', name: 'Local employment — apprenticeships',     unit: 'per FTE',  price: 21500, proxy: 'HACT' },
    { id: 'c2', code: 'NS2', name: 'Skills training delivered to NEET adults', unit: 'per hour', price: 38,    proxy: 'HACT' },
    { id: 'c3', code: 'NS3', name: 'Procurement spend with VCSE suppliers',  unit: 'per £',    price: 1.04,  proxy: 'TOMs' },
    { id: 'c4', code: 'NS4', name: 'Carbon emissions reduction',             unit: 'per tCO₂', price: 87,    proxy: 'TOMs' },
    { id: 'c5', code: 'NS5', name: 'STEM volunteering hours',                unit: 'per hour', price: 32,    proxy: 'HACT' },
    { id: 'c6', code: 'NS6', name: 'Community grant funding',                unit: 'per £',    price: 1.15,  proxy: 'TOMs' },
  ],

  activity: [
    { who: 'Bouygues UK',           did: 'submitted Q3 evidence pack',   when: '2h ago',    icon: 'bi-file-earmark-arrow-up' },
    { who: 'Sarah Mendez',          did: 'approved Veolia quarterly report', when: 'Yesterday', icon: 'bi-check2-circle' },
    { who: 'Northern Care Alliance', did: 'uploaded 14 case studies',     when: '2 days ago', icon: 'bi-images' },
    { who: 'System',                did: 'flagged Capita as at-risk for breach', when: '3 days ago', icon: 'bi-exclamation-triangle' },
  ],
};
