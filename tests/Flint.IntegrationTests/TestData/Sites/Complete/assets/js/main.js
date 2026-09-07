/**
 * Flint 完整测试站点 - 主 JavaScript 文件
 * 包含各种常用功能的示例代码
 */

// 严格模式
'use strict';

/**
 * DOM 加载完成后执行
 */
document.addEventListener('DOMContentLoaded', function() {
    console.log('Flint 测试站点已加载');
    
    // 初始化各个模块
    initNavigation();
    initScrollToTop();
    initCodeHighlight();
    initExternalLinks();
});

/**
 * 导航功能
 * 处理移动端导航菜单的展开/收起
 */
function initNavigation() {
    const navToggle = document.querySelector('.nav-toggle');
    const navMenu = document.querySelector('.nav-menu');
    
    if (navToggle && navMenu) {
        navToggle.addEventListener('click', function() {
            navMenu.classList.toggle('is-active');
            navToggle.classList.toggle('is-active');
        });
    }
    
    // 点击导航链接后关闭菜单
    const navLinks = document.querySelectorAll('.nav-link');
    navLinks.forEach(function(link) {
        link.addEventListener('click', function() {
            if (navMenu) {
                navMenu.classList.remove('is-active');
            }
            if (navToggle) {
                navToggle.classList.remove('is-active');
            }
        });
    });
}

/**
 * 返回顶部按钮
 */
function initScrollToTop() {
    // 创建返回顶部按钮
    const scrollTopBtn = document.createElement('button');
    scrollTopBtn.className = 'scroll-to-top';
    scrollTopBtn.innerHTML = '↑';
    scrollTopBtn.setAttribute('aria-label', '返回顶部');
    scrollTopBtn.style.cssText = `
        position: fixed;
        bottom: 20px;
        right: 20px;
        width: 40px;
        height: 40px;
        border: none;
        border-radius: 50%;
        background-color: #0066cc;
        color: white;
        font-size: 20px;
        cursor: pointer;
        opacity: 0;
        visibility: hidden;
        transition: opacity 0.3s, visibility 0.3s;
        z-index: 1000;
    `;
    document.body.appendChild(scrollTopBtn);
    
    // 监听滚动事件
    window.addEventListener('scroll', function() {
        if (window.pageYOffset > 300) {
            scrollTopBtn.style.opacity = '1';
            scrollTopBtn.style.visibility = 'visible';
        } else {
            scrollTopBtn.style.opacity = '0';
            scrollTopBtn.style.visibility = 'hidden';
        }
    });
    
    // 点击返回顶部
    scrollTopBtn.addEventListener('click', function() {
        window.scrollTo({
            top: 0,
            behavior: 'smooth'
        });
    });
}

/**
 * 代码高亮增强
 * 添加复制代码按钮
 */
function initCodeHighlight() {
    const codeBlocks = document.querySelectorAll('pre code');
    
    codeBlocks.forEach(function(codeBlock) {
        const pre = codeBlock.parentElement;
        
        // 创建复制按钮
        const copyBtn = document.createElement('button');
        copyBtn.className = 'copy-code-btn';
        copyBtn.textContent = '复制';
        copyBtn.style.cssText = `
            position: absolute;
            top: 5px;
            right: 5px;
            padding: 4px 8px;
            font-size: 12px;
            border: none;
            border-radius: 3px;
            background-color: #6c757d;
            color: white;
            cursor: pointer;
            opacity: 0;
            transition: opacity 0.3s;
        `;
        
        // 设置 pre 为相对定位
        pre.style.position = 'relative';
        pre.appendChild(copyBtn);
        
        // 鼠标悬停显示按钮
        pre.addEventListener('mouseenter', function() {
            copyBtn.style.opacity = '1';
        });
        
        pre.addEventListener('mouseleave', function() {
            copyBtn.style.opacity = '0';
        });
        
        // 复制功能
        copyBtn.addEventListener('click', function() {
            const code = codeBlock.textContent;
            navigator.clipboard.writeText(code).then(function() {
                copyBtn.textContent = '已复制!';
                setTimeout(function() {
                    copyBtn.textContent = '复制';
                }, 2000);
            }).catch(function(err) {
                console.error('复制失败:', err);
            });
        });
    });
}

/**
 * 外部链接处理
 * 为外部链接添加 target="_blank" 和安全属性
 */
function initExternalLinks() {
    const links = document.querySelectorAll('a[href^="http"]');
    const currentHost = window.location.host;
    
    links.forEach(function(link) {
        const href = link.getAttribute('href');
        try {
            const url = new URL(href);
            if (url.host !== currentHost) {
                link.setAttribute('target', '_blank');
                link.setAttribute('rel', 'noopener noreferrer');
            }
        } catch (e) {
            // 忽略无效 URL
        }
    });
}

/**
 * 工具函数：防抖
 * @param {Function} func - 要执行的函数
 * @param {number} wait - 等待时间（毫秒）
 * @returns {Function} - 防抖后的函数
 */
function debounce(func, wait) {
    let timeout;
    return function executedFunction(...args) {
        const later = function() {
            clearTimeout(timeout);
            func(...args);
        };
        clearTimeout(timeout);
        timeout = setTimeout(later, wait);
    };
}

/**
 * 工具函数：节流
 * @param {Function} func - 要执行的函数
 * @param {number} limit - 时间限制（毫秒）
 * @returns {Function} - 节流后的函数
 */
function throttle(func, limit) {
    let inThrottle;
    return function(...args) {
        if (!inThrottle) {
            func.apply(this, args);
            inThrottle = true;
            setTimeout(function() {
                inThrottle = false;
            }, limit);
        }
    };
}

// 导出工具函数（如果使用模块系统）
if (typeof module !== 'undefined' && module.exports) {
    module.exports = {
        debounce: debounce,
        throttle: throttle
    };
}
