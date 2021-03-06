﻿using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FreeSql;
using Admin.Core.Common.Configs;
using Admin.Core.Common.Helpers;
using Admin.Core.Model;
using Admin.Core.Common.Auth;

namespace Admin.Core.Db
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加数据库
        /// </summary>
        /// <param name="services"></param>
        /// <param name="env"></param>
        public async static void AddDb(this IServiceCollection services, IHostEnvironment env)
        {
            var dbConfig = new ConfigHelper().Get<DbConfig>("dbconfig", env.EnvironmentName);

            //创建数据库
            if (dbConfig.CreateDb)
            {
                await DbHelper.CreateDatabase(dbConfig);
            }

            #region FreeSql
            var freeSqlBuilder = new FreeSqlBuilder()
                    .UseConnectionString(dbConfig.Type, dbConfig.ConnectionString)
                    .UseAutoSyncStructure(dbConfig.SyncStructure)
                    .UseLazyLoading(false)
                    .UseNoneCommandParameter(true);

            #region 监听所有命令
            if (dbConfig.MonitorCommand)
            {
                freeSqlBuilder.UseMonitorCommand(cmd => { }, (cmd, traceLog) =>
                {
                    //Console.WriteLine($"{cmd.CommandText}\n{traceLog}\r\n");
                    Console.WriteLine($"{cmd.CommandText}\r\n");
                });
            }
            #endregion

            var fsql = freeSqlBuilder.Build();

            #region 初始化数据库
            //同步结构
            if (dbConfig.SyncStructure)
            {
                DbHelper.SyncStructure(fsql, dbType: dbConfig.Type.ToString());
            }

            //同步数据
            if (dbConfig.SyncData)
            {
                await DbHelper.SyncData(fsql);
            }
            #endregion

            //生成数据包
            if (dbConfig.GenerateData && !dbConfig.CreateDb && !dbConfig.SyncData)
            {
                await DbHelper.GenerateSimpleJsonData(fsql);
            }

            #region 监听Curd操作
            if (dbConfig.Curd)
            {
                fsql.Aop.CurdBefore += (s, e) =>
                {
                    Console.WriteLine($"{e.Sql}\r\n");
                };
            }
            #endregion

            #region 审计数据
            //计算服务器时间
            //var serverTime = fsql.Select<T>().Limit(1).First(a => DateTime.local);
            //var timeOffset = DateTime.UtcNow.Subtract(serverTime); 
            fsql.Aop.AuditValue += (s, e) =>
            {
                var user = services.BuildServiceProvider().GetService<IUser>();

                if (e.AuditValueType == FreeSql.Aop.AuditValueType.Insert)
                {
                    switch (e.Property.Name)
                    {
                        case "CreatedUserId":
                            e.Value = user?.Id;
                            break;
                        case "CreatedUserName":
                            e.Value = user?.Name;
                            break;
                            //case "CreatedTime":
                            //    e.Value = DateTime.Now.Subtract(timeOffset);
                            //    break;
                    }
                }
                else if (e.AuditValueType == FreeSql.Aop.AuditValueType.Update)
                {
                    switch (e.Property.Name)
                    {
                        case "ModifiedUserId":
                            e.Value = user?.Id;
                            break;
                        case "ModifiedUserName":
                            e.Value = user?.Name;
                            break;
                            //case "ModifiedTime":
                            //    e.Value = DateTime.Now.Subtract(timeOffset);
                            //    break;
                    }
                }
            };
            #endregion

            services.AddSingleton(fsql);
            services.AddFreeRepository(filter => filter.Apply<IEntitySoftDelete>("SoftDelete", a => a.IsDeleted == false));
            services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<IFreeSql>().CreateUnitOfWork());
            #endregion
        }
    }
}
