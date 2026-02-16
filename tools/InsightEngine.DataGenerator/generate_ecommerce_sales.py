import pandas as pd
import numpy as np
from faker import Faker
import random
from datetime import datetime, timedelta

fake = Faker('pt_BR')

def generate_ecommerce_sales(num_rows=5000):
    data = []

    # Distribuições não homogêneas
    statuses = ['Concluído', 'Pendente', 'Cancelado', 'Devolvido']
    status_weights = [0.75, 0.15, 0.08, 0.02]  # Maioria concluída

    payment_methods = ['Cartão de Crédito', 'Boleto', 'PIX', 'Transferência']
    payment_weights = [0.5, 0.25, 0.2, 0.05]

    channels = ['Site', 'App Mobile', 'Marketplace', 'Loja Física']
    channel_weights = [0.4, 0.3, 0.2, 0.1]

    categories = ['Eletrônicos', 'Roupas', 'Casa e Jardim', 'Livros', 'Esportes']
    category_weights = [0.3, 0.25, 0.2, 0.15, 0.1]

    for i in range(num_rows):
        order_date = fake.date_between(start_date='-2y', end_date='today')
        delivery_date = order_date + timedelta(days=random.randint(1, 15)) if random.random() > 0.1 else None

        # Valores com distribuição normal
        quantity = max(1, int(np.random.normal(2, 1.5)))
        unit_price = round(np.random.normal(150, 80), 2)
        freight = round(np.random.normal(15, 8), 2)
        discount = round(random.uniform(0, unit_price * 0.3), 2) if random.random() > 0.7 else 0

        total = (quantity * unit_price) + freight - discount

        row = {
            'ID_Pedido': f'PED{i+1:06d}',
            'Data_Pedido': order_date.strftime('%Y-%m-%d'),
            'Cliente_ID': f'CLI{random.randint(1, 10000):05d}',
            'Produto': fake.sentence(nb_words=3)[:-1],  # Remove ponto final
            'Quantidade': quantity,
            'Preco_Unitario': unit_price,
            'Total': round(total, 2),
            'Status': np.random.choice(statuses, p=status_weights),
            'Metodo_Pagamento': np.random.choice(payment_methods, p=payment_weights),
            'Frete': freight,
            'Desconto': discount,
            'Canal_Venda': np.random.choice(channels, p=channel_weights),
            'Cidade': fake.city(),
            'Estado': fake.state_abbr(),
            'Avaliacao': random.randint(1, 5) if random.random() > 0.3 else None,
            'Data_Entrega': delivery_date.strftime('%Y-%m-%d') if delivery_date else None,
            'Motivo_Cancelamento': fake.sentence() if random.random() > 0.9 else None,
            'Valor_Devolvido': round(random.uniform(0, total), 2) if random.random() > 0.95 else 0,
            'Categoria_Produto': np.random.choice(categories, p=category_weights),
            'SKU': f'SKU{random.randint(10000, 99999)}'
        }
        data.append(row)

    df = pd.DataFrame(data)
    df.to_csv('../../samples/vendas_ecommerce.csv', index=False, encoding='utf-8-sig')
    print(f"Arquivo 'vendas_ecommerce.csv' gerado com {num_rows} linhas.")

if __name__ == "__main__":
    generate_ecommerce_sales()