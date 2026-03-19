# Cloud-Based Property Management System
云端物业管理系统

This project is a cloud-based property management system developed using HTML, CSS, JavaScript for the frontend and ASP.NET Core for the backend. The system is deployed on AWS and integrates multiple cloud services to demonstrate scalability, automation, and monitoring.
本项目是一个基于云的物业管理系统，前端采用 HTML、CSS、JavaScript，后端使用 ASP.NET Core。系统部署在 AWS 云平台，并集成多种云服务，展示可扩展性、自动化和监控能力。

---

## Project Overview
项目简介

Managing rental properties manually can be inefficient and error-prone. Many landlords still rely on spreadsheets or messaging apps to track tenants, payments, and maintenance issues.
人工管理租赁物业效率低且容易出错，许多房东仍依赖表格或聊天工具记录租客、付款和维修。

This system aims to solve those problems by providing a centralized platform where landlords and tenants can manage property-related activities more effectively.
本系统通过集中平台，帮助房东和租客更高效地管理与物业相关的事务。

---

## Objectives
项目目标
- Develop a web-based property management system
   开发基于网页的物业管理系统
- Deploy the application on AWS cloud infrastructure
   在 AWS 云基础设施上部署应用
- Integrate cloud services such as storage, serverless computing, and monitoring
   集成云服务如存储、无服务器计算和监控
- Demonstrate a scalable and maintainable cloud architecture
   展示可扩展、易维护的云架构

---

## User Roles
用户角色

1. **Admin**
    - Manage users (landlords and tenants)
       管理用户（房东和租客）
    - View system overview and reports
       查看系统概览和报表

2. **Landlord**
    - Add and manage properties
       添加和管理物业
    - View tenant details
       查看租客信息
    - Handle maintenance requests
       处理维修请求
    - Upload documents
       上传文件
    - View payment records
       查看付款记录
   

3. **Tenant**
    - View assigned property
       查看分配的物业
    - Submit maintenance requests
       提交维修请求
    - Upload documents
       上传文件
    - Upload payment records
       上传付款记录
   

---

## Features
功能特性

### Authentication
- User registration and login
   用户注册与登录
- Role-based access control
   基于角色的权限控制

### Property Management
- Add, edit, delete properties
   添加、编辑、删除物业
- Assign tenants to properties
   分配租客到物业

### Tenant Management
- Store tenant information
   存储租客信息
- Link tenants to specific properties
   关联租客与物业

### Maintenance Request System
- Tenants can submit maintenance requests and payment records
   租客可提交维修请求和付款记录
- Landlords can approve or update request status
   房东可审批或更新请求状态
- Status tracking (Pending, Approved, Completed)
   状态跟踪（待处理、已批准、已完成）

### Document Management
- Upload and store files (e.g. tenancy agreements)
   上传和存储文件（如租赁合同）
- Files are stored in cloud storage (AWS S3)
   文件存储在云端（AWS S3）

### Payment Tracking
- Record rental payments
   记录租金支付
- View payment history
   查看支付历史

---

## Cloud Architecture
云架构

### Task 1 (Basic Deployment)
- Application hosted on AWS EC2
   应用部署在 AWS EC2
- Backend connected to AWS RDS (MySQL)
   后端连接 AWS RDS（MySQL）

Client → ASP.NET Application → AWS EC2 → AWS RDS
客户端 → ASP.NET 应用 → AWS EC2 → AWS RDS

### Task 2 (Cloud Integration)

The system is enhanced with serverless and cloud services:
系统进一步集成无服务器和云服务：
- API Gateway
   API 网关
- AWS Lambda
   AWS Lambda
- AWS S3
   AWS S3
- AWS SNS
   AWS SNS
- CloudWatch
   CloudWatch
- AWS X-Ray
   AWS X-Ray

---

## Example Workflows
示例流程

### Maintenance Notification
维修通知流程

Tenant submits request
租客提交请求
→ ASP.NET backend
→ ASP.NET 后端
→ API Gateway
→ API 网关
→ Lambda
→ Lambda
→ SNS
→ SNS
→ Email sent to landlord
→ 邮件通知房东

### File Upload
文件上传流程

User uploads document
用户上传文件
→ API Gateway
→ API 网关
→ Lambda
→ Lambda
→ Stored in S3 bucket
→ 存储到 S3 桶

---

## Database Design
数据库设计

Main tables include:
主要数据表包括：
- Users 用户
- Properties 物业
- Tenants 租客
- MaintenanceRequests 维修请求
- Payments 支付
- Documents 文件

---

## Technologies Used
技术栈

### Frontend
- HTML
   HTML
- CSS
   CSS
- JavaScript
   JavaScript
- Bootstrap
   Bootstrap

### Backend
- ASP.NET Core MVC
   ASP.NET Core MVC

### Cloud Services
- AWS EC2
   AWS EC2
- AWS RDS (MySQL)
   AWS RDS（MySQL）
- AWS S3
   AWS S3
- AWS Lambda
   AWS Lambda
- AWS API Gateway
   AWS API 网关
- AWS SNS
   AWS SNS
- AWS CloudWatch
   AWS CloudWatch
- AWS X-Ray
   AWS X-Ray

---

## Deployment Steps (Summary)
部署步骤（摘要）
1. Create AWS EC2 instance
   创建 AWS EC2 实例
2. Install .NET runtime
   安装 .NET 运行环境
3. Deploy ASP.NET application
   部署 ASP.NET 应用
4. Configure AWS RDS database
   配置 AWS RDS 数据库
5. Connect backend to RDS
   后端连接 RDS
6. Set up S3 bucket for file storage
   设置 S3 桶用于文件存储
7. Configure Lambda and API Gateway
   配置 Lambda 和 API 网关
8. Enable CloudWatch monitoring
   启用 CloudWatch 监控

---

## Monitoring
监控
- CloudWatch is used to monitor system performance
   CloudWatch 用于监控系统性能
- X-Ray is used for tracing API requests and debugging
   X-Ray 用于追踪 API 请求和调试

---

## Demo Flow
演示流程
1. User logs in
   用户登录
2. Landlord adds property
   房东添加物业
3. Admin assigns tenant
   管理员分配租客
4. Tenant submits maintenance request
   租客提交维修请求
5. System triggers notification
   系统触发通知
6. Landlord updates request status
   房东更新请求状态
7. User uploads document to S3
   用户上传文件到 S3

---

## Conclusion
总结

This project demonstrates how cloud computing can be used to build a scalable and efficient property management system. By integrating multiple AWS services, the system is able to handle real-world scenarios such as file storage, notifications, and monitoring.
本项目展示了如何利用云计算构建可扩展、高效的物业管理系统。通过集成多种 AWS 服务，系统能够应对实际场景，如文件存储、通知和监控。

---

## Project Access
项目访问

URL: (add your deployed link here)
项目地址：（请填写你的部署链接）
Test Account: (optional)
测试账号：（可选）

---

## Future Improvements
未来改进
- Mobile application version
   移动端版本
- Payment gateway integration
   集成支付网关
- Advanced analytics dashboard
   高级分析仪表盘
- Role-based security enhancements
   角色权限安全增强

---
