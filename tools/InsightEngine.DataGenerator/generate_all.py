#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import sys
import os

# Adicionar o diretório atual ao path para importar os módulos
sys.path.append(os.path.dirname(os.path.abspath(__file__)))

# Importar todos os geradores
from generate_ecommerce_sales import generate_ecommerce_sales
from generate_controllership import generate_controllership_data
from generate_hr import generate_hr_data
from generate_logistics import generate_logistics_data
from generate_marketing import generate_marketing_data
from generate_production import generate_production_data
from generate_inventory import generate_inventory_data
from generate_customers import generate_customers_data
from generate_suppliers import generate_suppliers_data
from generate_cashflow import generate_cashflow_data

def main():
    print("Iniciando geração de dados CSV...")
    print("=" * 50)

    try:
        # Executar todos os geradores
        generate_ecommerce_sales()
        generate_controllership_data()
        generate_hr_data()
        generate_logistics_data()
        generate_marketing_data()
        generate_production_data()
        generate_inventory_data()
        generate_customers_data()
        generate_suppliers_data()
        generate_cashflow_data()

        print("=" * 50)
        print("Todos os arquivos CSV foram gerados com sucesso!")
        print("Arquivos salvos em: ../../samples/")

    except ImportError as e:
        print(f"Erro de importação: {e}")
        print("Certifique-se de que todas as dependências estão instaladas:")
        print("pip install faker pandas numpy")
        sys.exit(1)
    except Exception as e:
        print(f"Erro durante a geração: {e}")
        sys.exit(1)

if __name__ == "__main__":
    main()