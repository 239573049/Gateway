﻿// <auto-generated />
using System;
using FastGateway.Service.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace FastGateway.Service.Migrations
{
    [DbContext(typeof(MasterContext))]
    [Migration("20240805184015_UpdateClientRequest")]
    partial class UpdateClientRequest
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "9.0.0-preview.6.24327.4");

            modelBuilder.Entity("FastGateway.Entities.ApplicationLogger", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("Country")
                        .HasColumnType("TEXT");

                    b.Property<string>("Domain")
                        .HasColumnType("TEXT");

                    b.Property<long>("Elapsed")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Extend")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Ip")
                        .HasColumnType("TEXT");

                    b.Property<string>("Method")
                        .HasColumnType("TEXT");

                    b.Property<string>("Path")
                        .HasColumnType("TEXT");

                    b.Property<string>("Platform")
                        .HasColumnType("TEXT");

                    b.Property<string>("Region")
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("RequestTime")
                        .HasColumnType("TEXT");

                    b.Property<int>("StatusCode")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("Success")
                        .HasColumnType("INTEGER");

                    b.Property<string>("UserAgent")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("application_logger", (string)null);
                });

            modelBuilder.Entity("FastGateway.Entities.BlacklistAndWhitelist", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("Description")
                        .HasMaxLength(200)
                        .HasColumnType("TEXT");

                    b.Property<bool>("Enable")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER")
                        .HasDefaultValue(true);

                    b.Property<string>("Ips")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<bool>("IsBlacklist")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("Enable");

                    b.HasIndex("Ips");

                    b.HasIndex("IsBlacklist");

                    b.HasIndex("Name");

                    b.ToTable("blacklist_and_whitelist", (string)null);
                });

            modelBuilder.Entity("FastGateway.Entities.Cert", b =>
                {
                    b.Property<string>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<bool>("AutoRenew")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Certs")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("CreateTime")
                        .HasColumnType("TEXT");

                    b.Property<string>("Domain")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Email")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<bool>("Expired")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Issuer")
                        .HasColumnType("TEXT");

                    b.Property<DateTime?>("NotAfter")
                        .HasColumnType("TEXT");

                    b.Property<byte>("RenewStats")
                        .HasColumnType("INTEGER");

                    b.Property<DateTime?>("RenewTime")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("cert", (string)null);
                });

            modelBuilder.Entity("FastGateway.Entities.ClientRequestLogger", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("Country")
                        .HasColumnType("TEXT");

                    b.Property<int>("Fail")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Ip")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("TEXT");

                    b.Property<string>("Region")
                        .HasColumnType("TEXT");

                    b.Property<string>("RequestTime")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("Success")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Total")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.ToTable("client_request_logger", (string)null);
                });

            modelBuilder.Entity("FastGateway.Entities.DomainName", b =>
                {
                    b.Property<string>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("Domains")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<bool>("Enable")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Headers")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Path")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Root")
                        .HasColumnType("TEXT");

                    b.Property<string>("ServerId")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Service")
                        .HasColumnType("TEXT");

                    b.Property<int>("ServiceType")
                        .HasColumnType("INTEGER");

                    b.Property<string>("TryFiles")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("UpStreams")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("Enable");

                    b.HasIndex("Root");

                    b.HasIndex("ServerId");

                    b.HasIndex("Service");

                    b.HasIndex("ServiceType");

                    b.ToTable("domain_name", (string)null);
                });

            modelBuilder.Entity("FastGateway.Entities.RateLimit", b =>
                {
                    b.Property<string>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<bool>("Enable")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER")
                        .HasDefaultValue(true);

                    b.Property<string>("Endpoint")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("EndpointWhitelist")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("IpWhitelist")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("Limit")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Period")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("Enable");

                    b.ToTable("rate_limit", (string)null);
                });

            modelBuilder.Entity("FastGateway.Entities.Server", b =>
                {
                    b.Property<string>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<bool>("CopyRequestHost")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Description")
                        .HasColumnType("TEXT");

                    b.Property<bool>("Enable")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER")
                        .HasDefaultValue(true);

                    b.Property<bool>("EnableBlacklist")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("EnableTunnel")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("EnableWhitelist")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("IsHttps")
                        .HasColumnType("INTEGER");

                    b.Property<ushort>("Listen")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<bool>("RedirectHttps")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("StaticCompress")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("Enable");

                    b.ToTable("server", (string)null);
                });

            modelBuilder.Entity("FastGateway.Entities.Setting", b =>
                {
                    b.Property<string>("Key")
                        .HasMaxLength(50)
                        .HasColumnType("TEXT");

                    b.Property<string>("Description")
                        .HasMaxLength(200)
                        .HasColumnType("TEXT");

                    b.Property<string>("Group")
                        .HasMaxLength(50)
                        .HasColumnType("TEXT");

                    b.Property<bool>("IsPublic")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("IsSystem")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Value")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("TEXT");

                    b.HasKey("Key");

                    b.HasIndex("IsPublic");

                    b.HasIndex("IsSystem");

                    b.ToTable("setting", (string)null);
                });
#pragma warning restore 612, 618
        }
    }
}
